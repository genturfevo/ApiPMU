using Microsoft.EntityFrameworkCore;

namespace ApiPMU.Models
{
    /// <summary>
    /// Entités représentant la base de données genturfevo.
    /// </summary>
    public partial class ApiPMUDbContext : DbContext
    {
        public ApiPMUDbContext(DbContextOptions<ApiPMUDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Reunion> Reunions { get; set; }

        public virtual DbSet<Course> Courses { get; set; }

        public virtual DbSet<Cheval> Chevaux { get; set; }

        public virtual DbSet<EntraineurJokey> EntraineurJokeys { get; set; }

        public virtual DbSet<Performance> Performances { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Reunion>(entity =>
            {
                entity.HasNoKey();

                entity.Property(e => e.DateModif).HasPrecision(0);
                entity.Property(e => e.DateReunion).HasPrecision(0);
                entity.Property(e => e.LieuCourse).HasMaxLength(50);
                entity.Property(e => e.NumGeny)
                    .IsRequired()
                    .HasMaxLength(50);
            });

            modelBuilder.Entity<Course>(entity =>
            {
                entity.HasNoKey();

                entity.Property(e => e.Age).HasDefaultValue((short)0);
                entity.Property(e => e.Allocation).HasDefaultValue(0);
                entity.Property(e => e.Cinquieme).HasDefaultValue((short)0).HasColumnName("cinquieme");
                entity.Property(e => e.Cordage).HasMaxLength(50);
                entity.Property(e => e.CoupleGagnant).HasDefaultValue(0f);
                entity.Property(e => e.CouplePlace12).HasDefaultValue(0f);
                entity.Property(e => e.CouplePlace13).HasDefaultValue(0f);
                entity.Property(e => e.CouplePlace23).HasDefaultValue(0f);
                entity.Property(e => e.CptStats).HasDefaultValue(0);
                entity.Property(e => e.DateModif).HasPrecision(0);
                entity.Property(e => e.Deuxieme).HasDefaultValue((short)0).HasColumnName("deuxieme");
                entity.Property(e => e.Discipline).HasMaxLength(20);
                entity.Property(e => e.Distance).HasDefaultValue(0);
                entity.Property(e => e.Jcouples).HasColumnName("JCouples");
                entity.Property(e => e.Jmulti).HasColumnName("JMulti");
                entity.Property(e => e.Jquinte).HasColumnName("JQuinte");
                entity.Property(e => e.Jtrio).HasColumnName("JTrio");
                entity.Property(e => e.Libelle).HasMaxLength(255);
                entity.Property(e => e.Multi4).HasDefaultValue(0f);
                entity.Property(e => e.Multi5).HasDefaultValue(0f);
                entity.Property(e => e.Multi6).HasDefaultValue(0f);
                entity.Property(e => e.Multi7).HasDefaultValue(0f);
                entity.Property(e => e.NumGeny).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Partants).HasDefaultValue(0);
                entity.Property(e => e.Premier).HasDefaultValue((short)0).HasColumnName("premier");
                entity.Property(e => e.Quadrio2).HasDefaultValue(0f);
                entity.Property(e => e.Quadrio3).HasDefaultValue(0f);
                entity.Property(e => e.QuadrioD).HasDefaultValue(0f);
                entity.Property(e => e.QuadrioO).HasDefaultValue(0f);
                entity.Property(e => e.Quarte3).HasDefaultValue(0f);
                entity.Property(e => e.QuarteD).HasDefaultValue(0f);
                entity.Property(e => e.QuarteO).HasDefaultValue(0f);
                entity.Property(e => e.Quatrieme).HasDefaultValue((short)0).HasColumnName("quatrieme");
                entity.Property(e => e.Quinte3).HasDefaultValue(0f);
                entity.Property(e => e.Quinte4).HasDefaultValue(0f);
                entity.Property(e => e.Quinte45).HasDefaultValue(0f);
                entity.Property(e => e.QuinteD).HasDefaultValue(0f);
                entity.Property(e => e.QuinteO).HasDefaultValue(0f);
                entity.Property(e => e.SimpleGagnant).HasDefaultValue(0f);
                entity.Property(e => e.SimplePlace1).HasDefaultValue(0f);
                entity.Property(e => e.SimplePlace2).HasDefaultValue(0f);
                entity.Property(e => e.SimplePlace3).HasDefaultValue(0f);
                entity.Property(e => e.SsmaTimeStamp).IsRequired().IsRowVersion().IsConcurrencyToken().HasColumnName("SSMA_TimeStamp");
                entity.Property(e => e.Sur24).HasDefaultValue(0f).HasColumnName("sur24");
                entity.Property(e => e.TierceD).HasDefaultValue(0f);
                entity.Property(e => e.TierceO).HasDefaultValue(0f);
                entity.Property(e => e.Trio).HasDefaultValue(0f);
                entity.Property(e => e.Troisieme).HasDefaultValue((short)0).HasColumnName("troisieme");
                entity.Property(e => e.TypeCourse).HasMaxLength(50);
            });

            modelBuilder.Entity<Cheval>(entity =>
            {
                entity.HasNoKey().ToTable("Chevaux");

                entity.Property(e => e.Avis).HasMaxLength(255);
                entity.Property(e => e.ClaAr).HasDefaultValue(0).HasColumnName("ClaAR");
                entity.Property(e => e.ClaCfp).HasDefaultValue(0).HasColumnName("ClaCFP");
                entity.Property(e => e.ClaCote).HasDefaultValue(0);
                entity.Property(e => e.ClaCrifx).HasDefaultValue(0).HasColumnName("ClaCRIFX");
                entity.Property(e => e.ClaCx).HasDefaultValue(0).HasColumnName("ClaCX");
                entity.Property(e => e.ClaHisto).HasDefaultValue(0);
                entity.Property(e => e.ClaIdc).HasDefaultValue(0).HasColumnName("ClaIDC");
                entity.Property(e => e.ClaOr).HasDefaultValue(0).HasColumnName("ClaOR");
                entity.Property(e => e.ClaRub).HasDefaultValue(0);
                entity.Property(e => e.ClaTmatic).HasDefaultValue(0).HasColumnName("ClaTMatic");
                entity.Property(e => e.ClaZone).HasDefaultValue((short)0);
                entity.Property(e => e.ClasCoefReussite).HasDefaultValue(0.0);
                entity.Property(e => e.ClasEd).HasColumnName("ClasED");
                entity.Property(e => e.ClasHmp).HasColumnName("ClasHMP");
                entity.Property(e => e.ClasMn).HasDefaultValue(0).HasColumnName("ClasMN");
                entity.Property(e => e.ClasPt).HasDefaultValue(0).HasColumnName("ClasPT");
                entity.Property(e => e.ClasPtsStats).HasDefaultValue(0);
                entity.Property(e => e.ClasRc).HasColumnName("ClasRC");
                entity.Property(e => e.ClasRd).HasDefaultValue(0).HasColumnName("ClasRD");
                entity.Property(e => e.ClasRe).HasDefaultValue(0).HasColumnName("ClasRE");
                entity.Property(e => e.ClasStats).HasDefaultValue(0);
                entity.Property(e => e.ClasSynth).HasDefaultValue(0);
                entity.Property(e => e.ClasValStats).HasDefaultValue(0);
                entity.Property(e => e.CoefReussite).HasDefaultValue(0f);
                entity.Property(e => e.Corde).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Cote11h).HasDefaultValue(0f);
                entity.Property(e => e.CoteDef).HasDefaultValue(0f);
                entity.Property(e => e.CoteGen).HasDefaultValue(0f);
                entity.Property(e => e.DateModif).HasPrecision(0);
                entity.Property(e => e.Deferre).HasMaxLength(50);
                entity.Property(e => e.Entraineur).IsRequired().HasMaxLength(50);
                entity.Property(e => e.IndFor).HasDefaultValue(0f);
                entity.Property(e => e.IndForme).HasDefaultValue(0.0);
                entity.Property(e => e.Ja).HasColumnName("JA");
                entity.Property(e => e.Jokey).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Median).HasMaxLength(1);
                entity.Property(e => e.Musique).HasMaxLength(20);
                entity.Property(e => e.NbBloc).HasDefaultValue(0);
                entity.Property(e => e.NbCouples).HasDefaultValue(0);
                entity.Property(e => e.NbCourses).HasDefaultValue(0);
                entity.Property(e => e.NbPlaces).HasDefaultValue(0);
                entity.Property(e => e.NbVictoires).HasDefaultValue(0);
                entity.Property(e => e.Nom).HasMaxLength(50);
                entity.Property(e => e.NumGeny).IsRequired().HasMaxLength(50);
                entity.Property(e => e.PlaceArrivee).HasDefaultValue((short)0);
                entity.Property(e => e.Pourcent11h).HasDefaultValue(0f);
                entity.Property(e => e.PourcentDef).HasDefaultValue(0f);
                entity.Property(e => e.PrcCh).HasDefaultValue(0f);
                entity.Property(e => e.PrcDr).HasDefaultValue(0);
                entity.Property(e => e.PrcEn).HasDefaultValue(0);
                entity.Property(e => e.PtsAr).HasDefaultValue(0).HasColumnName("PtsAR");
                entity.Property(e => e.PtsCfp).HasDefaultValue(0f).HasColumnName("PtsCFP");
                entity.Property(e => e.PtsCrifx).HasDefaultValue(0).HasColumnName("PtsCRIFX");
                entity.Property(e => e.PtsCx).HasDefaultValue(0f).HasColumnName("PtsCX");
                entity.Property(e => e.PtsEd).HasColumnName("PtsED");
                entity.Property(e => e.PtsHisto).HasDefaultValue(0);
                entity.Property(e => e.PtsIdc).HasDefaultValue(0f).HasColumnName("PtsIDC");
                entity.Property(e => e.PtsMn).HasDefaultValue(0).HasColumnName("PtsMN");
                entity.Property(e => e.PtsOr).HasDefaultValue(0f).HasColumnName("PtsOR");
                entity.Property(e => e.PtsPt).HasDefaultValue(0).HasColumnName("PtsPT");
                entity.Property(e => e.PtsRc).HasColumnName("PtsRC");
                entity.Property(e => e.PtsRd).HasDefaultValue(0).HasColumnName("PtsRD");
                entity.Property(e => e.PtsRe).HasDefaultValue(0).HasColumnName("PtsRE");
                entity.Property(e => e.PtsRub).HasDefaultValue(0);
                entity.Property(e => e.PtsSynth).HasDefaultValue(0);
                entity.Property(e => e.PtsTmatic).HasDefaultValue(0).HasColumnName("PtsTMatic");
                entity.Property(e => e.Record).HasMaxLength(255);
                entity.Property(e => e.RecordDate).HasPrecision(0);
                entity.Property(e => e.RecordDistance).HasDefaultValue(0);
                entity.Property(e => e.RecordFerrage).HasMaxLength(255);
                entity.Property(e => e.RecordPlace).HasDefaultValue(0);
                entity.Property(e => e.SexAge).IsRequired().HasMaxLength(50);
                entity.Property(e => e.SsmaTimeStamp).IsRequired().IsRowVersion().IsConcurrencyToken().HasColumnName("SSMA_TimeStamp");
                entity.Property(e => e.VaCouples).HasDefaultValue(0f);
                entity.Property(e => e.ValStats).HasDefaultValue(0);
                entity.Property(e => e.ZoneAbc).IsRequired().HasMaxLength(1).HasDefaultValueSql("((0))").HasColumnName("ZoneABC");
            });

            modelBuilder.Entity<EntraineurJokey>(entity =>
            {
                entity.HasNoKey().ToTable("EntraineurJokey");
                entity.Property(e => e.DateModif).HasPrecision(0);
                entity.Property(e => e.Entjok).IsRequired().HasMaxLength(50);
                entity.Property(e => e.NbCr).HasColumnName("NbCR");
                entity.Property(e => e.Nom).IsRequired().HasMaxLength(50);
                entity.Property(e => e.NumGeny).IsRequired().HasMaxLength(50);
            });

            modelBuilder.Entity<Performance>(entity =>
            {
                entity.HasNoKey();
                entity.Property(e => e.Avis).HasMaxLength(255);
                entity.Property(e => e.Cordage).HasMaxLength(50);
                entity.Property(e => e.Corde).HasMaxLength(50);
                entity.Property(e => e.DateModif).HasPrecision(0);
                entity.Property(e => e.DatePerf).HasPrecision(0);
                entity.Property(e => e.Deferre).HasMaxLength(255);
                entity.Property(e => e.Discipline).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Lieu).HasMaxLength(50);
                entity.Property(e => e.Nom).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Place).HasMaxLength(50);
                entity.Property(e => e.RedKdist).HasMaxLength(50).HasColumnName("RedKDist");
                entity.Property(e => e.TypeCourse).HasMaxLength(50);
                entity.Property(e => e.Video).HasMaxLength(255);
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
    /// <summary>
    /// Entité représentant un programme PMU.
    /// </summary>
    public class Programme
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }

        // Stockage brut des données JSON récupérées depuis l'API PMU.
        public string DataJson { get; set; } = string.Empty;

        // Vous pouvez ajouter d'autres colonnes pour des données spécifiques ou des métadonnées.
    }
}
