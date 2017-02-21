namespace Conference.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class InitialSchema : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "ConferenceManagement.Conferences",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        AccessCode = c.String(maxLength: 6),
                        OwnerName = c.String(nullable: false),
                        OwnerEmail = c.String(nullable: false),
                        Slug = c.String(nullable: false),
                        WasEverPublished = c.Boolean(nullable: false),
                        Name = c.String(nullable: false),
                        Description = c.String(nullable: false),
                        Location = c.String(nullable: false),
                        Tagline = c.String(),
                        TwitterSearch = c.String(),
                        StartDate = c.DateTime(nullable: false),
                        EndDate = c.DateTime(nullable: false),
                        IsPublished = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "ConferenceManagement.SeatTypes",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        Name = c.String(nullable: false, maxLength: 70),
                        Description = c.String(nullable: false, maxLength: 250),
                        Quantity = c.Int(nullable: false),
                        Price = c.Decimal(nullable: false, precision: 18, scale: 2),
                        ConferenceInfo_Id = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("ConferenceManagement.Conferences", t => t.ConferenceInfo_Id, cascadeDelete: true)
                .Index(t => t.ConferenceInfo_Id);
            
            CreateTable(
                "ConferenceManagement.Orders",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        ConferenceId = c.Guid(nullable: false),
                        AssignmentsId = c.Guid(),
                        AccessCode = c.String(),
                        RegistrantName = c.String(),
                        RegistrantEmail = c.String(),
                        TotalAmount = c.Decimal(nullable: false, precision: 18, scale: 2),
                        Status = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "ConferenceManagement.OrderSeats",
                c => new
                    {
                        OrderId = c.Guid(nullable: false),
                        Position = c.Int(nullable: false),
                        Attendee_FirstName = c.String(),
                        Attendee_LastName = c.String(),
                        Attendee_Email = c.String(),
                        SeatInfoId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => new { t.OrderId, t.Position })
                .ForeignKey("ConferenceManagement.SeatTypes", t => t.SeatInfoId, cascadeDelete: true)
                .ForeignKey("ConferenceManagement.Orders", t => t.OrderId, cascadeDelete: true)
                .Index(t => t.OrderId)
                .Index(t => t.SeatInfoId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("ConferenceManagement.OrderSeats", "OrderId", "ConferenceManagement.Orders");
            DropForeignKey("ConferenceManagement.OrderSeats", "SeatInfoId", "ConferenceManagement.SeatTypes");
            DropForeignKey("ConferenceManagement.SeatTypes", "ConferenceInfo_Id", "ConferenceManagement.Conferences");
            DropIndex("ConferenceManagement.OrderSeats", new[] { "SeatInfoId" });
            DropIndex("ConferenceManagement.OrderSeats", new[] { "OrderId" });
            DropIndex("ConferenceManagement.SeatTypes", new[] { "ConferenceInfo_Id" });
            DropTable("ConferenceManagement.OrderSeats");
            DropTable("ConferenceManagement.Orders");
            DropTable("ConferenceManagement.SeatTypes");
            DropTable("ConferenceManagement.Conferences");
        }
    }
}
