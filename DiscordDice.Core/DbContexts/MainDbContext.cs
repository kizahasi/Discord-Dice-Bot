using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordDice
{
    public class MainDbContext : DbContext
    {
        readonly IConfig _config;

        public MainDbContext(IConfig config) :base()
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            optionsBuilder.UseSqlite(_config.DatabaseConnectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Models.User>().HasKey(m => m.ID);
            modelBuilder.Entity<Models.Scan>().HasKey(m => m.ID);
            modelBuilder.Entity<Models.ScanRoll>().HasKey(r => new { r.ScanID, r.UserID });
            modelBuilder
                .Entity<Models.Scan>()
                .Property(e => e.Expr)
                .HasConversion(v => v.ToString(), s => Expr.Main.Interpret(s));
            modelBuilder
                .Entity<Models.Scan>()
                .Property(e => e.StartedAt)
                .HasConversion(new DateTimeOffsetToStringConverter());
        }

        internal DbSet<Models.User> Users { get; set; }

        internal DbSet<Models.Scan> Scans { get; set; }

        internal DbSet<Models.ScanRoll> ScanRolls { get; set; }

        public static DbContext GetInstance(IConfig config) => new MainDbContext(config);
        
    }
}
