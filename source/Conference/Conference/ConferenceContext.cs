using System.Data.Entity;
using System.Diagnostics;

namespace Conference
{
    /// <summary>
    ///     Data context for this ORM-based domain.
    /// </summary>
    public class ConferenceContext : DbContext
    {
        public static readonly string SchemaName = "ConferenceManagement";

        public virtual IDbSet<ConferenceInfo> Conferences { get; set; }

        public virtual IDbSet<SeatType> Seats { get; set; }
        
        public virtual IDbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema(SchemaName);

            modelBuilder.Entity<ConferenceInfo>().ToTable("Conferences");
            modelBuilder.Entity<ConferenceInfo>().HasMany(x => x.Seats).WithRequired();
            modelBuilder.Entity<SeatType>().ToTable("SeatTypes");
            modelBuilder.Entity<Order>().ToTable("Orders");
            modelBuilder.Entity<OrderSeat>().ToTable("OrderSeats");
            modelBuilder.Entity<OrderSeat>().HasKey(seat => new {seat.OrderId, seat.Position});
        }
    }
}