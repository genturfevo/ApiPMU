using Microsoft.EntityFrameworkCore;
using ApiPMU.Models;

namespace ApiPMU
{
    /// <summary>
    /// Contexte Entity Framework pour l'API PMU.
    /// Cette classe configure le mapping entre vos entités et les tables de la base de données.
    /// </summary>
    public class ApiPMUDbContext : DbContext
    {
        public ApiPMUDbContext(DbContextOptions<ApiPMUDbContext> options)
            : base(options)
        {
        }

        // Déclaration des DbSet pour chaque entité
        public DbSet<Reunion> Reunions { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Cheval> Chevaux { get; set; }
        public DbSet<EntraineurJokey> EntraineurJokey { get; set; }
        public DbSet<Performance> Performances { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // ------------------- Configuration de l'entité Reunion -------------------
            modelBuilder.Entity<Reunion>(entity =>
            {
                // Spécifie le nom de la table en base
                entity.ToTable("Reunions");

                // Clé primaire composite : NumGeny et NumReunion
                entity.HasKey(e => new { e.NumGeny, e.NumReunion });

                // Configuration de la colonne LieuCourse
                entity.Property(e => e.LieuCourse)
                      .HasMaxLength(100)
                      .IsRequired();

                // Configuration de la colonne DateReunion
                entity.Property(e => e.DateReunion)
                      .HasColumnType("datetime")
                      .IsRequired();

                // Configuration de la colonne DateModif
                entity.Property(e => e.DateModif)
                      .HasColumnType("datetime")
                      .IsRequired();
            });

            // ------------------- Configuration de l'entité Course -------------------
            modelBuilder.Entity<Course>(entity =>
            {
                // Nom de la table
                entity.ToTable("Courses");

                // Clé primaire composite : NumGeny et NumCourse
                entity.HasKey(e => new { e.NumGeny, e.NumCourse });

                // Propriété Discipline
                entity.Property(e => e.Discipline)
                      .HasMaxLength(50)
                      .IsRequired();

                // Propriétés pour les différents types de courses
                entity.Property(e => e.Jcouples)
                      .HasMaxLength(50);
                entity.Property(e => e.Jtrio)
                      .HasMaxLength(50);
                entity.Property(e => e.Jmulti)
                      .HasMaxLength(50);
                entity.Property(e => e.Jquinte)
                      .HasMaxLength(50);

                // Propriété Autostart (booléen)
                entity.Property(e => e.Autostart)
                      .IsRequired();

                // Propriété TypeCourse
                entity.Property(e => e.TypeCourse)
                      .HasMaxLength(50);

                // Propriété Cordage
                entity.Property(e => e.Cordage)
                      .HasMaxLength(50);

                // Propriété Allocation
                entity.Property(e => e.Allocation)
                      .HasMaxLength(50);

                // Propriété Distance
                entity.Property(e => e.Distance)
                      .HasColumnType("int");

                // Propriété Partants (smallint)
                entity.Property(e => e.Partants)
                      .HasColumnType("smallint");

                // Propriété Libelle
                entity.Property(e => e.Libelle)
                      .HasMaxLength(100);

                // Propriété DateModif
                entity.Property(e => e.DateModif)
                      .HasColumnType("datetime")
                      .IsRequired();
            });

            // ------------------- Configuration de l'entité Chevaux -------------------
            modelBuilder.Entity<Cheval>(entity =>
            {
                entity.ToTable("Chevaux");

                // Clé primaire (supposée être 'Id' ou 'NumCheval'; adaptez selon votre modèle)
                entity.HasKey(e => e.Id);

                // Colonne Nom (obligatoire, longueur maximale 100)
                entity.Property(e => e.Nom)
                      .HasMaxLength(100)
                      .IsRequired();

                // Exemple de colonnes supplémentaires : DateNaissance et Sexe
                entity.Property(e => e.DateNaissance)
                      .HasColumnType("date");
                entity.Property(e => e.Sexe)
                      .HasMaxLength(10);
            });

            // ------------------- Configuration de l'entité EntraineurJokey -------------------
            modelBuilder.Entity<EntraineurJokey>(entity =>
            {
                entity.ToTable("EntraineurJokey");

                // Clé primaire (supposée être 'Id')
                entity.HasKey(e => e.Id);

                // Colonne Nom (obligatoire, longueur maximale 100)
                entity.Property(e => e.Nom)
                      .HasMaxLength(100)
                      .IsRequired();

                // Exemple de colonne supplémentaire : Prenom
                entity.Property(e => e.Prenom)
                      .HasMaxLength(100);
            });

            // ------------------- Configuration de l'entité Performances -------------------
            modelBuilder.Entity<Performances>(entity =>
            {
                entity.ToTable("Performances");

                // Clé primaire (supposée être 'Id')
                entity.HasKey(e => e.Id);

                // Exemple de colonne Score (entier, obligatoire)
                entity.Property(e => e.Score)
                      .HasColumnType("int")
                      .IsRequired();

                // Exemple de colonne Rang (entier)
                entity.Property(e => e.Rang)
                      .HasColumnType("int");

                // Exemple de colonne Commentaire (chaîne, longueur maximale 250)
                entity.Property(e => e.Commentaire)
                      .HasMaxLength(250);

                // Exemple de colonne DatePerformance (datetime)
                entity.Property(e => e.DatePerformance)
                      .HasColumnType("datetime");
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
