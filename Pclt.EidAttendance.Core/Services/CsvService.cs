using System.Text;
using System.Globalization;
using System.Threading;
using Pclt.EidAttendance.Core.Models;

namespace Pclt.EidAttendance.Core.Services;

public class CsvService
{
    private readonly object _lock = new();
    private const char Delimiter = ';';
    private const string Header = "Deelnemer;Opleidingsnummer;Voornaam;Achternaam;Rijksregisternummer;Address;Postcode;Gemeente;Nationaliteit;Geslacht;GeboorteDatum;GeboortePlaats";
    private const int ReplaceRetryCount = 6;
    private const int ReplaceRetryDelayMs = 120;

    public void EnsureCsvExists(string csvPath)
    {
        var directory = Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (_lock)
        {
            if (!File.Exists(csvPath))
            {
                using var writer = new StreamWriter(csvPath, append: false, Encoding.UTF8);
                writer.WriteLine(Header);
            }
        }
    }

    public List<ParticipantRegistration> ReadAll(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            return new List<ParticipantRegistration>();
        }

        var registrations = new List<ParticipantRegistration>();
        var lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        var activeDelimiter = ResolveDelimiter(lines);

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parsedValues = ParseCsvLine(line, activeDelimiter);
            if (parsedValues.Length >= 11)
            {
                var hasTrainingNumber = parsedValues.Length >= 12;
                var offset = hasTrainingNumber ? 1 : 0;

                DateTime? birthDate = null;
                if (DateTime.TryParseExact(parsedValues[9 + offset], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedBirthDate))
                {
                    birthDate = parsedBirthDate;
                }

                registrations.Add(new ParticipantRegistration
                {
                    ParticipantNumber = int.TryParse(parsedValues[0], out var participantNumber) ? participantNumber : index,
                    TrainingNumber = hasTrainingNumber ? parsedValues[1] : string.Empty,
                    FirstName = parsedValues[1 + offset],
                    LastName = parsedValues[2 + offset],
                    NationalNumber = parsedValues[3 + offset],
                    Address = parsedValues[4 + offset],
                    PostalCode = parsedValues[5 + offset],
                    City = parsedValues[6 + offset],
                    Nationality = parsedValues[7 + offset],
                    Gender = parsedValues[8 + offset],
                    BirthDate = birthDate,
                    BirthPlace = parsedValues[10 + offset],
                    ScanDateTime = DateTime.Now
                });

                continue;
            }

            var values = line.Split(activeDelimiter);
            if (values.Length < 5)
            {
                continue;
            }

            DateTime.TryParse(values[4], out var scanDateTime);

            var split = SplitName(values[0]);

            registrations.Add(new ParticipantRegistration
            {
                ParticipantNumber = index,
                TrainingNumber = string.Empty,
                FirstName = split.firstName,
                LastName = split.lastName,
                NationalNumber = values[1],
                Address = string.Empty,
                PostalCode = string.Empty,
                City = string.Empty,
                Nationality = string.Empty,
                Gender = string.Empty,
                BirthDate = null,
                BirthPlace = string.Empty,
                ScanDateTime = scanDateTime
            });
        }

        return registrations;
    }

    public void SaveAll(string csvPath, IEnumerable<ParticipantRegistration> registrations)
    {
        var directory = Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (_lock)
        {
            var tempPath = Path.Combine(
                directory ?? AppDomain.CurrentDomain.BaseDirectory,
                $".{Path.GetFileName(csvPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                {
                    writer.WriteLine(Header);

                    foreach (var registration in registrations)
                    {
                        writer.WriteLine(ToCsvLine(registration));
                    }

                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(csvPath))
                {
                    ReplaceFileWithRetry(tempPath, csvPath);
                }
                else
                {
                    File.Move(tempPath, csvPath, overwrite: true);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }

    private static void ReplaceFileWithRetry(string sourcePath, string destinationPath)
    {
        IOException? lastException = null;

        for (var attempt = 1; attempt <= ReplaceRetryCount; attempt++)
        {
            try
            {
                File.Replace(sourcePath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
                if (attempt == ReplaceRetryCount)
                {
                    break;
                }

                Thread.Sleep(ReplaceRetryDelayMs);
            }
        }

        throw new IOException($"CSV kon niet opgeslagen worden omdat het bestand tijdelijk in gebruik was: {destinationPath}", lastException);
    }

    public void AppendRegistration(string csvPath, ParticipantRegistration r)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
        var line = ToCsvLine(r);

        lock (_lock)
        {
            var newFile = !File.Exists(csvPath);
            using var writer = new StreamWriter(csvPath, append: true, Encoding.UTF8);
            if (newFile) writer.WriteLine(Header);
            writer.WriteLine(line);
        }
    }

    private static string ToCsvLine(ParticipantRegistration registration)
    {
        return string.Join(Delimiter,
            Escape(registration.ParticipantNumber.ToString(CultureInfo.InvariantCulture)),
            Escape(registration.TrainingNumber),
            Escape(registration.FirstName),
            Escape(registration.LastName),
            Escape(registration.NationalNumber),
            Escape(registration.Address),
            Escape(registration.PostalCode),
            Escape(registration.City),
            Escape(registration.Nationality),
            Escape(registration.Gender),
            Escape(registration.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty),
            Escape(registration.BirthPlace));
    }

    private static string Escape(string? value)
    {
        var normalized = value ?? string.Empty;
        if (!normalized.Contains(Delimiter) && !normalized.Contains('"') && !normalized.Contains('\n') && !normalized.Contains('\r'))
        {
            return normalized;
        }

        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }

    private static string[] ParseCsvLine(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == delimiter)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        values.Add(current.ToString());
        return values.ToArray();
    }

    private static char ResolveDelimiter(string[] lines)
    {
        if (lines.Length == 0)
        {
            return Delimiter;
        }

        var header = lines[0];
        var semicolonCount = header.Count(ch => ch == ';');
        var commaCount = header.Count(ch => ch == ',');

        if (semicolonCount == 0 && commaCount == 0)
        {
            return Delimiter;
        }

        return semicolonCount >= commaCount ? ';' : ',';
    }

    private static (string firstName, string lastName) SplitName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (string.Empty, string.Empty);
        }

        var tokens = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1)
        {
            return (tokens[0], string.Empty);
        }

        return (string.Join(' ', tokens[..^1]), tokens[^1]);
    }
}
