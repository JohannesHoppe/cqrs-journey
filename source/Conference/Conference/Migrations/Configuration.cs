using System.Data.Entity.Migrations;

namespace Conference.Migrations
{
    internal sealed class Configuration : DbMigrationsConfiguration<ConferenceContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
            ContextKey = "conference";
        }
    }
}