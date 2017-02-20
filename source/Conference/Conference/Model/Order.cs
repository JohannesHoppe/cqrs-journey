using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace Conference
{
    public class Order
    {
        public enum OrderStatus
        {
            Pending,
            Paid
        }

        [Key]
        public Guid Id { get; set; }

        public Guid ConferenceId { get; set; }

        /// <summary>
        ///     Used for correlating with the seat assignments.
        /// </summary>
        public Guid? AssignmentsId { get; set; }

        [Display(Name = "Order Code")]
        public string AccessCode { get; set; }

        [Display(Name = "Registrant Name")]
        public string RegistrantName { get; set; }

        [Display(Name = "Registrant Email")]
        public string RegistrantEmail { get; set; }

        [Display(Name = "Total Amount")]
        public decimal TotalAmount { get; set; }

        public OrderStatus Status { get; set; }

        public ICollection<OrderSeat> Seats { get; set; }

        public Order(Guid conferenceId, Guid orderId, string accessCode)
            : this()
        {
            Id = orderId;
            ConferenceId = conferenceId;
            AccessCode = accessCode;
        }

        protected Order()
        {
            Seats = new ObservableCollection<OrderSeat>();
        }
    }
}