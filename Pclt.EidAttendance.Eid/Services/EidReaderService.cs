using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using Net.Pkcs11Interop.HighLevelAPI.Factories;
using Pclt.EidAttendance.Eid.Models;
using Pclt.EidAttendance.Eid.Validation;

namespace Pclt.EidAttendance.Eid.Services;

public class EidReaderService
{
    private readonly EidReaderOptions _options;

    public EidReaderService(EidReaderOptions? options = null)
    {
        _options = options ?? EidReaderOptions.Default;
    }

    public IReadOnlyList<string> GetConnectedReaders()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Array.Empty<string>();
        }

        var contextResult = SCardEstablishContext(SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out var context);
        if (contextResult != SCARD_S_SUCCESS)
        {
            return Array.Empty<string>();
        }

        try
        {
            uint readersBufferLength = 0;
            var readersListResult = SCardListReaders(context, null, null, ref readersBufferLength);
            if (readersListResult == SCARD_E_NO_READERS_AVAILABLE || readersBufferLength == 0)
            {
                return Array.Empty<string>();
            }

            if (readersListResult != SCARD_S_SUCCESS)
            {
                return Array.Empty<string>();
            }

            var readersBuffer = new byte[readersBufferLength];
            readersListResult = SCardListReaders(context, null, readersBuffer, ref readersBufferLength);
            if (readersListResult != SCARD_S_SUCCESS)
            {
                return Array.Empty<string>();
            }

            var readersRaw = System.Text.Encoding.Unicode.GetString(readersBuffer);
            return readersRaw
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
        }
        finally
        {
            SCardReleaseContext(context);
        }
    }

    public bool HasAuthorizedReaderConnected()
    {
        return GetAuthorizedReaders().Length > 0;
    }

    public string GetReaderDiagnosticsSummary()
    {
        var readers = GetConnectedReaders();
        if (readers.Count == 0)
        {
            return "Geen readers gedetecteerd via Windows Smart Card API.";
        }

        var authorized = GetAuthorizedReaders();
        return $"Gedetecteerd: {string.Join(" | ", readers)}. Gebruikt: {string.Join(" | ", authorized)}.";
    }

    public async Task<EidIdentityModel> ReadOnceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout moet groter dan 0 zijn.");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Belgische eID wordt momenteel enkel op Windows ondersteund.");
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsAnyCardPresentOnAuthorizedReader())
            {
                break;
            }

            await Task.Delay(250, cancellationToken);
        }

        if (!IsAnyCardPresentOnAuthorizedReader())
        {
            throw new EidReaderNotFoundException("Geen eID-kaart gedetecteerd in een geautoriseerde eID-reader.");
        }

        var identity = await TryReadIdentityFromRealCardAsync(timeout, cancellationToken);
        if (identity is null)
        {
            if (!_options.AllowMockFallback)
            {
                throw new EidIntegrationNotConfiguredException(
                    "Reader gedetecteerd maar echte kaartuitlezing is nog niet geconfigureerd. Integreer Belgische eID middleware/SDK in TryReadIdentityFromRealCardAsync().");
            }

            identity = GenerateMockIdentity();
        }

        if (!BelgianNationalNumberValidator.IsValid(identity.NationalNumber))
        {
            throw new EidValidationException("Ongeldig rijksregisternummer gelezen van de eID.");
        }

        if (string.IsNullOrWhiteSpace(identity.FullName))
        {
            throw new EidValidationException("Naam ontbreekt in eID-data.");
        }

        return identity;
    }

    public bool IsAnyCardPresentOnAuthorizedReader()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        if (IsAnyCardPresentViaPkcs11())
        {
            return true;
        }

        var authorizedReaders = GetAuthorizedReaders();

        if (authorizedReaders.Length == 0)
        {
            return false;
        }

        var contextResult = SCardEstablishContext(SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out var context);
        if (contextResult != SCARD_S_SUCCESS)
        {
            return false;
        }

        try
        {
            var states = authorizedReaders
                .Select(reader => new SCARD_READERSTATE
                {
                    szReader = reader,
                    dwCurrentState = SCARD_STATE_UNAWARE,
                    rgbAtr = new byte[36]
                })
                .ToArray();

            var statusResult = SCardGetStatusChange(context, 0, states, (uint)states.Length);
            if (statusResult != SCARD_S_SUCCESS)
            {
                return false;
            }

            return states.Any(state => (state.dwEventState & SCARD_STATE_PRESENT) == SCARD_STATE_PRESENT);
        }
        finally
        {
            SCardReleaseContext(context);
        }
    }

    private bool IsAnyCardPresentViaPkcs11()
    {
        try
        {
            var pkcs11Path = ResolvePkcs11LibraryPath();
            if (string.IsNullOrWhiteSpace(pkcs11Path) || !File.Exists(pkcs11Path))
            {
                return false;
            }

            var factories = new Pkcs11InteropFactories();
            using var library = factories.Pkcs11LibraryFactory.LoadPkcs11Library(factories, pkcs11Path, AppType.MultiThreaded);
            var slotsWithCard = library.GetSlotList(SlotsType.WithTokenPresent);
            return slotsWithCard is not null && slotsWithCard.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private string[] GetAuthorizedReaders()
    {
        var readers = GetConnectedReaders().ToArray();
        if (readers.Length == 0)
        {
            return Array.Empty<string>();
        }

        if (_options.AuthorizedReaderKeywords.Count == 0)
        {
            return readers;
        }

        var matches = readers
            .Where(reader =>
                _options.AuthorizedReaderKeywords.Any(keyword =>
                    reader.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return matches.Length > 0 ? matches : readers;
    }

    private async Task<EidIdentityModel?> TryReadIdentityFromRealCardAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return ReadIdentityViaBelgianMiddleware();
            }
            catch (EidIntegrationNotConfiguredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(250, cancellationToken);
        }

        if (lastException is not null)
        {
            throw new EidReaderException($"Kaart gevonden, maar uitlezen via Belgische middleware mislukt: {lastException.Message}");
        }

        return null;
    }

    private EidIdentityModel ReadIdentityViaBelgianMiddleware()
    {
        var pkcs11Path = ResolvePkcs11LibraryPath();
        if (string.IsNullOrWhiteSpace(pkcs11Path))
        {
            throw new EidIntegrationNotConfiguredException(
                "Belgische eID middleware niet gevonden. Installeer eID middleware en/of zet EID_PKCS11_PATH naar beidpkcs11.dll.");
        }

        var factories = new Pkcs11InteropFactories();
        using var library = factories.Pkcs11LibraryFactory.LoadPkcs11Library(factories, pkcs11Path, AppType.MultiThreaded);

        var slotsWithCard = library.GetSlotList(SlotsType.WithTokenPresent);
        if (slotsWithCard is null || slotsWithCard.Count == 0)
        {
            throw new EidReaderNotFoundException("Geen kaart gevonden in Belgische eID middleware.");
        }

        var preferredSlot = slotsWithCard
            .FirstOrDefault(slot =>
            {
                var slotInfo = slot.GetSlotInfo();
                return _options.AuthorizedReaderKeywords.Any(keyword =>
                    slotInfo.SlotDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }) ?? slotsWithCard.First();

        using var session = preferredSlot.OpenSession(SessionType.ReadOnly);

        var certificateClassTemplate = new List<IObjectAttribute>
        {
            factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE)
        };

        session.FindObjectsInit(certificateClassTemplate);
        var certificateObjects = session.FindObjects(20);
        session.FindObjectsFinal();

        foreach (var certObject in certificateObjects)
        {
            var attributes = session.GetAttributeValue(certObject, new List<CKA> { CKA.CKA_VALUE });
            var rawCertificate = attributes[0].GetValueAsByteArray();
            if (rawCertificate is null || rawCertificate.Length == 0)
            {
                continue;
            }

            using var certificate = new X509Certificate2(rawCertificate);
            var identity = TryBuildIdentityFromCertificate(certificate);
            if (identity is not null)
            {
                EnrichIdentityFromMiddlewareData(session, factories, identity);
                return identity;
            }
        }

        throw new EidValidationException("Geen geldig rijksregisternummer/naam gevonden op eID-certificaten.");
    }

    private static void EnrichIdentityFromMiddlewareData(ISession session, Pkcs11InteropFactories factories, EidIdentityModel identity)
    {
        identity.FirstName = PreferNonEmpty(ReadDataObjectString(session, factories, "firstnames"), identity.FirstName);
        identity.LastName = PreferNonEmpty(ReadDataObjectString(session, factories, "surname"), identity.LastName);
        identity.NationalNumber = PreferNonEmpty(
            BelgianNationalNumberValidator.Normalize(ReadDataObjectString(session, factories, "national_number") ?? string.Empty),
            identity.NationalNumber);
        identity.Address = PreferNonEmpty(ReadDataObjectString(session, factories, "address_street_and_number"), identity.Address);
        identity.PostalCode = PreferNonEmpty(ReadDataObjectString(session, factories, "address_zip"), identity.PostalCode);
        identity.City = PreferNonEmpty(ReadDataObjectString(session, factories, "address_municipality"), identity.City);
        identity.Nationality = PreferNonEmpty(ReadDataObjectString(session, factories, "nationality"), identity.Nationality);
        identity.BirthPlace = PreferNonEmpty(ReadDataObjectString(session, factories, "location_of_birth"), identity.BirthPlace);
        identity.Gender = PreferNonEmpty(ReadDataObjectString(session, factories, "gender"), identity.Gender);

        var firstLetterOfThirdGivenName = ReadDataObjectString(session, factories, "first_letter_of_third_given_name");
        if (!string.IsNullOrWhiteSpace(firstLetterOfThirdGivenName) &&
            !identity.FirstName.Contains(firstLetterOfThirdGivenName, StringComparison.OrdinalIgnoreCase))
        {
            identity.FirstName = $"{identity.FirstName} {firstLetterOfThirdGivenName}".Trim();
        }

        identity.FullName = $"{identity.FirstName} {identity.LastName}".Trim();
    }

    private static string PreferNonEmpty(string? preferredValue, string existingValue)
    {
        return string.IsNullOrWhiteSpace(preferredValue) ? existingValue : preferredValue.Trim();
    }

    private static string? ReadDataObjectString(ISession session, Pkcs11InteropFactories factories, string label)
    {
        var template = new List<IObjectAttribute>
        {
            factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_DATA),
            factories.ObjectAttributeFactory.Create(CKA.CKA_LABEL, label)
        };

        session.FindObjectsInit(template);
        try
        {
            var objects = session.FindObjects(1);
            if (objects.Count == 0)
            {
                return null;
            }

            var attributes = session.GetAttributeValue(objects[0], new List<CKA> { CKA.CKA_VALUE });
            var rawValue = attributes[0].GetValueAsByteArray();
            if (rawValue is null || rawValue.Length == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(rawValue)
                .TrimEnd('\0')
                .Trim();
        }
        finally
        {
            session.FindObjectsFinal();
        }
    }

    private static EidIdentityModel? TryBuildIdentityFromCertificate(X509Certificate2 certificate)
    {
        var subject = certificate.Subject;

        var serialNumber = ExtractDistinguishedNameValue(subject, "SERIALNUMBER");
        var normalizedSerial = BelgianNationalNumberValidator.Normalize(serialNumber ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedSerial) || normalizedSerial.Length < 11)
        {
            return null;
        }

        var givenName = ExtractDistinguishedNameValue(subject, "G")
            ?? ExtractDistinguishedNameValue(subject, "GN")
            ?? ExtractDistinguishedNameValue(subject, "GIVENNAME");
        var surname = ExtractDistinguishedNameValue(subject, "SN")
            ?? ExtractDistinguishedNameValue(subject, "SURNAME");
        var commonName = ExtractDistinguishedNameValue(subject, "CN");

        var fullName = $"{givenName} {surname}".Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = commonName ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(givenName) || string.IsNullOrWhiteSpace(surname))
        {
            var splitName = SplitName(fullName);
            givenName = string.IsNullOrWhiteSpace(givenName) ? splitName.firstName : givenName;
            surname = string.IsNullOrWhiteSpace(surname) ? splitName.lastName : surname;
        }

        return new EidIdentityModel
        {
            FullName = fullName,
            FirstName = givenName ?? string.Empty,
            LastName = surname ?? string.Empty,
            NationalNumber = normalizedSerial,
            Address = string.Empty,
            PostalCode = string.Empty,
            City = string.Empty,
            Nationality = string.Empty,
            Gender = string.Empty,
            BirthDate = null,
            BirthPlace = string.Empty,
            PhotoBytes = null
        };
    }

    private static (string firstName, string lastName) SplitName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (string.Empty, string.Empty);
        }

        var tokens = fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        if (tokens.Length == 1)
        {
            return (tokens[0], string.Empty);
        }

        return (string.Join(' ', tokens[..^1]), tokens[^1]);
    }

    private static string? ExtractDistinguishedNameValue(string subject, string field)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var pattern = $@"(?:^|,\s*){Regex.Escape(field)}\s*=\s*([^,]+)";
        var match = Regex.Match(subject, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ResolvePkcs11LibraryPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("EID_PKCS11_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var baseDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Belgium Identity Card"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Belgium Identity Card"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Belgium Identity Card", "FireFox Plugin Manifests"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Belgium Identity Card", "EidViewer"),
            Environment.GetFolderPath(Environment.SpecialFolder.System)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var fileNames = Environment.Is64BitProcess
            ? new[]
            {
                "beid_ff_pkcs11_64.dll",
                "beid_ff_pkcs11.dll",
                "beidpkcs11.dll",
                "beid_ff_pkcs11_32.dll"
            }
            : new[]
            {
                "beid_ff_pkcs11_32.dll",
                "beid_ff_pkcs11.dll",
                "beidpkcs11.dll",
                "beid_ff_pkcs11_64.dll"
            };

        foreach (var baseDir in baseDirs)
        {
            foreach (var fileName in fileNames)
            {
                var fullPath = Path.Combine(baseDir, fileName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static EidIdentityModel GenerateMockIdentity()
    {
        var baseDate = DateTime.Today.AddYears(-30);
        var sequence = 1;
        var nationalNumber = BelgianNationalNumberValidator.BuildValid(baseDate, sequence);

        return new EidIdentityModel
        {
            FullName = $"TEST GEBRUIKER {DateTime.Now:HHmmss}",
            FirstName = "TEST",
            LastName = $"GEBRUIKER {DateTime.Now:HHmmss}",
            NationalNumber = nationalNumber,
            Address = string.Empty,
            PostalCode = string.Empty,
            City = string.Empty,
            Nationality = "Belg",
            Gender = string.Empty,
            BirthDate = baseDate,
            BirthPlace = string.Empty,
            PhotoBytes = null
        };
    }

    private const uint SCARD_SCOPE_USER = 0x0000;
    private const int SCARD_S_SUCCESS = 0x00000000;
    private const int SCARD_E_NO_READERS_AVAILABLE = unchecked((int)0x8010002E);
    private const uint SCARD_STATE_UNAWARE = 0x00000000;
    private const uint SCARD_STATE_PRESENT = 0x00000020;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SCARD_READERSTATE
    {
        [MarshalAs(UnmanagedType.LPTStr)]
        public string szReader;

        public IntPtr pvUserData;
        public uint dwCurrentState;
        public uint dwEventState;
        public uint cbAtr;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
        public byte[] rgbAtr;
    }

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    private static extern int SCardEstablishContext(uint dwScope, IntPtr notUsed1, IntPtr notUsed2, out IntPtr phContext);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    private static extern int SCardListReaders(IntPtr hContext, string? mszGroups, byte[]? mszReaders, ref uint pcchReaders);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    private static extern int SCardGetStatusChange(IntPtr hContext, uint dwTimeout, [In, Out] SCARD_READERSTATE[] rgReaderStates, uint cReaders);

    [DllImport("winscard.dll")]
    private static extern int SCardReleaseContext(IntPtr hContext);
}
