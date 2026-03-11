namespace Pclt.EidAttendance.Core.Models;

public class ParticipantRegistration
{
    public int ParticipantNumber { get; set; }
    public string TrainingNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string NationalNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public string BirthPlace { get; set; } = string.Empty;
    public DateTime ScanDateTime { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}
