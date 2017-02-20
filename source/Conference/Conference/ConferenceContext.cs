using System.Data.Entity;

namespace Conference
{
    /// <summary>
    ///     Data context for this ORM-based domain.
    /// </summary>
    public class ConferenceContext : DbContext
    {
        public const string SchemaName = "ConferenceManagement";

        public virtual DbSet<ConferenceInfo> Conferences { get; set; }

        public virtual DbSet<SeatType> Seats { get; set; }

        public virtual DbSet<Order> Orders { get; set; }

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