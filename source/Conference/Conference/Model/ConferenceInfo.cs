using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Conference.Common.Utils;
using Conference.Properties;
using Infrastructure.Utils;

namespace Conference
{
    /// <summary>
    ///     The full conference information.
    /// </summary>
    /// <remarks>
    ///     This class inherits from <see cref="EditableConferenceInfo" />
    ///     and exposes more information that is not user-editable once
    ///     it has been generated or provided.
    /// </remarks>
    public class ConferenceInfo : EditableConferenceInfo
    {
        public Guid Id { get; set; }

        [StringLength(6, MinimumLength = 6)]
        public string AccessCode { get; set; }

        [Display(Name = "Owner")]
        [Required]
        public string OwnerName { get; set; }

        [Display(Name = "Email")]
        [Required]
        [EmailAddress(ErrorMessageResourceType = typeof(Resources), ErrorMessageResourceName = "InvalidEmail")]
        public string OwnerEmail { get; set; }

        [Required]
        [RegularExpression(@"^\w+$", ErrorMessageResourceType = typeof(Resources), ErrorMessageResourceName = "InvalidSlug")]
        public string Slug { get; set; }

        public bool WasEverPublished { get; set; }

        public virtual ICollection<SeatType> Seats { get; set; }

        public ConferenceInfo()
        {
            Id = GuidUtil.NewSequentialId();
            Seats = new ObservableCollection<SeatType>();
            AccessCode = HandleGenerator.Generate(6);
        }
    }
}