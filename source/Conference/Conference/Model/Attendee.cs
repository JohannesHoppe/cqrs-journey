using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conference
{
    /// <summary>
    ///     Represents an attendee to the conference, someone who has been
    ///     assigned to a purchased seat.
    /// </summary>
    [ComplexType]
    public class Attendee
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        [EmailAddress]
        public string Email { get; set; }
    }
}