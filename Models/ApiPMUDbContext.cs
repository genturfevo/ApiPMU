using Microsoft.EntityFrameworkCore;

namespace ApiPMU.Models
{
    /// <summary>
    /// Contexte Entity Framework pour l'API PMU.
    /// Ce contexte mappe exactement les entités sur leurs tables correspondantes en respectant :
    /// - Reunions : clé unique sur NumGeny
    /// - Courses : clé composite sur NumGeny et NumCourse
    /// - Chevaux : clé composite sur NumGeny, NumCourse et Numero
    /// - EntraineurJokey : clé composite sur NumGeny, Entjok et Nom
    /// - Performances : clé composite sur Nom, DatePerf et Discipline
    /// </summary>
    public class ApiPMUDbContext : DbContext
    {
        public ApiPMUDbContext(DbContextOptions<ApiPMUDbContext> options)
            : base(options)
        {
        }

        // DbSet déclarés selon les noms de tables et classes
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
                entity.ToTable("Reunions");
                entity.HasKey(e => e.NumGeny);  // clé unique sur NumGeny
                entity.Property(e => e.NumGeny).HasMaxLength(50).IsRequired();
                entity.Property(e => e.NumReunion).HasColumnType("int");
                entity.Property(e => e.LieuCourse).HasMaxLength(50);
                entity.Property(e => e.DateReunion).HasColumnType("datetime2");
                entity.Property(e => e.DateModif).HasColumnType("datetime2");
            });

            // ------------------- Configuration de l'entité Course -------------------
            modelBuilder.Entity<Course>(entity =>
            {
                entity.ToTable("Course");
                entity.HasNoKey();
                entity.Property(e => e.NumGeny).HasColumnType("nvarchar");  // NumGeny
                entity.Property(e => e.NumCourse).HasColumnType("smallint");  // NumCourse
                entity.Property(e => e.Discipline).HasColumnType("nvarchar");  // Discipline
                entity.Property(e => e.Difficulte).HasColumnType("real");  // Difficulte
                entity.Property(e => e.JCouples).HasColumnType("bit");  // JCouples
                entity.Property(e => e.JTrio).HasColumnType("bit");  // JTrio
                entity.Property(e => e.JMulti).HasColumnType("bit");  // JMulti
                entity.Property(e => e.JQuinte).HasColumnType("bit");  // JQuinte
                entity.Property(e => e.Autostart).HasColumnType("bit");  // Autostart
                entity.Property(e => e.TypeCourse).HasColumnType("nvarchar");  // TypeCourse
                entity.Property(e => e.Cordage).HasColumnType("nvarchar");  // Cordage
                entity.Property(e => e.Allocation).HasColumnType("int");  // Allocation
                entity.Property(e => e.Distance).HasColumnType("int");  // Distance
                entity.Property(e => e.Partants).HasColumnType("int");  // Partants
                entity.Property(e => e.Libelle).HasColumnType("nvarchar");  // Libelle
                entity.Property(e => e.Premier).HasColumnType("smallint");  // premier
                entity.Property(e => e.Deuxieme).HasColumnType("smallint");  // deuxieme
                entity.Property(e => e.Troisieme).HasColumnType("smallint");  // troisieme
                entity.Property(e => e.Quatrieme).HasColumnType("smallint");  // quatrieme
                entity.Property(e => e.Cinquieme).HasColumnType("smallint");  // cinquieme
                entity.Property(e => e.SimpleGagnant).HasColumnType("real");  // SimpleGagnant
                entity.Property(e => e.SimplePlace1).HasColumnType("real");  // SimplePlace1
                entity.Property(e => e.SimplePlace2).HasColumnType("real");  // SimplePlace2
                entity.Property(e => e.SimplePlace3).HasColumnType("real");  // SimplePlace3
                entity.Property(e => e.CoupleGagnant).HasColumnType("real");  // CoupleGagnant
                entity.Property(e => e.CouplePlace12).HasColumnType("real");  // CouplePlace12
                entity.Property(e => e.CouplePlace13).HasColumnType("real");  // CouplePlace13
                entity.Property(e => e.CouplePlace23).HasColumnType("real");  // CouplePlace23
                entity.Property(e => e.Trio).HasColumnType("real");  // Trio
                entity.Property(e => e.Sur24).HasColumnType("real");  // sur24
                entity.Property(e => e.TierceO).HasColumnType("real");  // TierceO
                entity.Property(e => e.TierceD).HasColumnType("real");  // TierceD
                entity.Property(e => e.QuarteO).HasColumnType("real");  // QuarteO
                entity.Property(e => e.QuarteD).HasColumnType("real");  // QuarteD
                entity.Property(e => e.Quarte3).HasColumnType("real");  // Quarte3
                entity.Property(e => e.QuinteO).HasColumnType("real");  // QuinteO
                entity.Property(e => e.QuinteD).HasColumnType("real");  // QuinteD
                entity.Property(e => e.Quinte4).HasColumnType("real");  // Quinte4
                entity.Property(e => e.Quinte45).HasColumnType("real");  // Quinte45
                entity.Property(e => e.Quinte3).HasColumnType("real");  // Quinte3
                entity.Property(e => e.Multi4).HasColumnType("real");  // Multi4
                entity.Property(e => e.Multi5).HasColumnType("real");  // Multi5
                entity.Property(e => e.Multi6).HasColumnType("real");  // Multi6
                entity.Property(e => e.Multi7).HasColumnType("real");  // Multi7
                entity.Property(e => e.QuadrioO).HasColumnType("real");  // QuadrioO
                entity.Property(e => e.QuadrioD).HasColumnType("real");  // QuadrioD
                entity.Property(e => e.Quadrio3).HasColumnType("real");  // Quadrio3
                entity.Property(e => e.Quadrio2).HasColumnType("real");  // Quadrio2
                entity.Property(e => e.CptStats).HasColumnType("int");  // CptStats
                entity.Property(e => e.Age).HasColumnType("smallint");  // Age
                entity.Property(e => e.DateModif).HasColumnType("datetime2");  // DateModif
            });

            // ------------------- Configuration de l'entité Cheval -------------------
            modelBuilder.Entity<Cheval>(entity =>
            {
                entity.ToTable("Chevaux");
                entity.HasNoKey();
                entity.Property(e => e.NumGeny).HasColumnType("nvarchar(50)");  // NumGeny
                entity.Property(e => e.NumCourse).HasColumnType("smallint");  // NumCourse
                entity.Property(e => e.Numero).HasColumnType("smallint");  // Numero
                entity.Property(e => e.Nom).HasColumnType("nvarchar(50)");  // Nom
                entity.Property(e => e.Corde).HasColumnType("nvarchar(50)");  // Corde
                entity.Property(e => e.SexAge).HasColumnType("nvarchar(50)");  // SexAge
                entity.Property(e => e.DistPoid).HasColumnType("real");  // DistPoid
                entity.Property(e => e.Deferre).HasColumnType("nvarchar(50)");  // Deferre
                entity.Property(e => e.Jokey).HasColumnType("nvarchar(50)");  // Jokey
                entity.Property(e => e.Entraineur).HasColumnType("nvarchar(50)");  // Entraineur
                entity.Property(e => e.Gains).HasColumnType("int");  // Gains
                entity.Property(e => e.ZoneABC).HasColumnType("nvarchar(1)");  // ZoneABC
                entity.Property(e => e.ClaZone).HasColumnType("smallint");  // ClaZone
                entity.Property(e => e.Cote11h).HasColumnType("real");  // Cote11h
                entity.Property(e => e.CoteDef).HasColumnType("real");  // CoteDef
                entity.Property(e => e.CoteGen).HasColumnType("real");  // CoteGen
                entity.Property(e => e.Pourcent11h).HasColumnType("real");  // Pourcent11h
                entity.Property(e => e.PourcentDef).HasColumnType("real");  // PourcentDef
                entity.Property(e => e.PlaceArrivee).HasColumnType("smallint");  // PlaceArrivee
                entity.Property(e => e.ClasCoefReussite).HasColumnType("float");  // ClasCoefReussite
                entity.Property(e => e.CoefReussite).HasColumnType("real");  // CoefReussite
                entity.Property(e => e.IndFor).HasColumnType("real");  // IndFor
                entity.Property(e => e.IndForme).HasColumnType("float");  // IndForme
                entity.Property(e => e.NbCourses).HasColumnType("int");  // NbCourses
                entity.Property(e => e.NbVictoires).HasColumnType("int");  // NbVictoires
                entity.Property(e => e.NbPlaces).HasColumnType("int");  // NbPlaces
                entity.Property(e => e.ClaCote).HasColumnType("int");  // ClaCote
                entity.Property(e => e.PtsOR).HasColumnType("real");  // PtsOR
                entity.Property(e => e.ClaOR).HasColumnType("int");  // ClaOR
                entity.Property(e => e.PtsAR).HasColumnType("int");  // PtsAR
                entity.Property(e => e.ClaAR).HasColumnType("int");  // ClaAR
                entity.Property(e => e.PtsCX).HasColumnType("real");  // PtsCX
                entity.Property(e => e.ClaCX).HasColumnType("int");  // ClaCX
                entity.Property(e => e.PtsCRIFX).HasColumnType("int");  // PtsCRIFX
                entity.Property(e => e.ClaCRIFX).HasColumnType("int");  // ClaCRIFX
                entity.Property(e => e.PtsTMatic).HasColumnType("int");  // PtsTMatic
                entity.Property(e => e.ClaTMatic).HasColumnType("int");  // ClaTMatic
                entity.Property(e => e.PtsHisto).HasColumnType("int");  // PtsHisto
                entity.Property(e => e.ClaHisto).HasColumnType("int");  // ClaHisto
                entity.Property(e => e.PtsRub).HasColumnType("int");  // PtsRub
                entity.Property(e => e.ClaRub).HasColumnType("int");  // ClaRub
                entity.Property(e => e.PrcCh).HasColumnType("real");  // PrcCh
                entity.Property(e => e.PrcDr).HasColumnType("int");  // PrcDr
                entity.Property(e => e.PrcEn).HasColumnType("int");  // PrcEn
                entity.Property(e => e.NbBloc).HasColumnType("int");  // NbBloc
                entity.Property(e => e.NbCouples).HasColumnType("int");  // NbCouples
                entity.Property(e => e.VaCouples).HasColumnType("real");  // VaCouples
                entity.Property(e => e.Median).HasColumnType("nvarchar(1)");  // Median
                entity.Property(e => e.ClasMN).HasColumnType("int");  // ClasMN
                entity.Property(e => e.ClasPT).HasColumnType("int");  // ClasPT
                entity.Property(e => e.ClasRE).HasColumnType("int");  // ClasRE
                entity.Property(e => e.ClasRD).HasColumnType("int");  // ClasRD
                entity.Property(e => e.ClaIDC).HasColumnType("int");  // ClaIDC
                entity.Property(e => e.ClaCFP).HasColumnType("int");  // ClaCFP
                entity.Property(e => e.PtsMN).HasColumnType("int");  // PtsMN
                entity.Property(e => e.PtsPT).HasColumnType("int");  // PtsPT
                entity.Property(e => e.PtsRD).HasColumnType("int");  // PtsRD
                entity.Property(e => e.PtsRE).HasColumnType("int");  // PtsRE
                entity.Property(e => e.PtsCFP).HasColumnType("real");  // PtsCFP
                entity.Property(e => e.PtsIDC).HasColumnType("real");  // PtsIDC
                entity.Property(e => e.ClasPtsStats).HasColumnType("int");  // ClasPtsStats
                entity.Property(e => e.PtsStats).HasColumnType("real");  // PtsStats
                entity.Property(e => e.ClasValStats).HasColumnType("int");  // ClasValStats
                entity.Property(e => e.ValStats).HasColumnType("int");  // ValStats
                entity.Property(e => e.ClasStats).HasColumnType("int");  // ClasStats
                entity.Property(e => e.CumStats).HasColumnType("real");  // CumStats
                entity.Property(e => e.PtsSynth).HasColumnType("int");  // PtsSynth
                entity.Property(e => e.ClasSynth).HasColumnType("int");  // ClasSynth
                entity.Property(e => e.RecordDate).HasColumnType("datetime2");  // RecordDate
                entity.Property(e => e.RecordPlace).HasColumnType("int");  // RecordPlace
                entity.Property(e => e.RecordDistance).HasColumnType("int");  // RecordDistance
                entity.Property(e => e.RecordFerrage).HasColumnType("nvarchar(255)");  // RecordFerrage
                entity.Property(e => e.Record).HasColumnType("nvarchar(255)");  // Record
                entity.Property(e => e.Avis).HasColumnType("nvarchar(255)");  // Avis
                entity.Property(e => e.DateModif).HasColumnType("datetime2");  // DateModif
                entity.Property(e => e.ClasHMP).HasColumnType("int");  // ClasHMP
                entity.Property(e => e.PtsED).HasColumnType("int");  // PtsED
                entity.Property(e => e.ClasED).HasColumnType("int");  // ClasED
                entity.Property(e => e.PtsR10).HasColumnType("int");  // PtsR10
                entity.Property(e => e.ClasR10).HasColumnType("int");  // ClasR10
                entity.Property(e => e.PtsRC).HasColumnType("int");  // PtsRC
                entity.Property(e => e.ClasRC).HasColumnType("int");  // ClasRC
                entity.Property(e => e.JA).HasColumnType("int");  // JA
                entity.Property(e => e.Musique).HasColumnType("nvarchar(20)");  // Musique
            });

            // ------------------- Configuration de l'entité EntraineurJokey -------------------
            modelBuilder.Entity<EntraineurJokey>(entity =>
            {
                entity.ToTable("EntraineurJokey");
                entity.HasNoKey();
                entity.Property(e => e.NumGeny).HasColumnType("nvarchar(50)");  // NumGeny
                entity.Property(e => e.Entjok).HasColumnType("nvarchar(50)");  // Entjok
                entity.Property(e => e.Nom).HasColumnType("nvarchar(50)");  // Nom
                entity.Property(e => e.NbCourses).HasColumnType("int");  // NbCourses
                entity.Property(e => e.NbVictoires).HasColumnType("int");  // NbVictoires
                entity.Property(e => e.Ecart).HasColumnType("int");  // Ecart
                entity.Property(e => e.NbCR).HasColumnType("smallint");  // NbCR
                entity.Property(e => e.DateModif).HasColumnType("datetime2");  // DateModif
            });

            // ------------------- Configuration de l'entité Performance -------------------
            modelBuilder.Entity<Performance>(entity =>
            {
                entity.ToTable("Performances");
                entity.HasNoKey();
                entity.Property(e => e.Nom).HasColumnType("nvarchar(50)");  // Nom
                entity.Property(e => e.DatePerf).HasColumnType("datetime2");  // DatePerf
                entity.Property(e => e.Lieu).HasColumnType("nvarchar(50)");  // Lieu
                entity.Property(e => e.Dist).HasColumnType("real");  // Dist
                entity.Property(e => e.Gains).HasColumnType("int");  // Gains
                entity.Property(e => e.Partants).HasColumnType("smallint");  // Partants
                entity.Property(e => e.Corde).HasColumnType("nvarchar(50)");  // Corde
                entity.Property(e => e.Cordage).HasColumnType("nvarchar(50)");  // Cordage
                entity.Property(e => e.Deferre).HasColumnType("nvarchar(255)");  // Deferre
                entity.Property(e => e.Poid).HasColumnType("real");  // Poid
                entity.Property(e => e.Discipline).HasColumnType("nvarchar(50)");  // Discipline
                entity.Property(e => e.TypeCourse).HasColumnType("nvarchar(50)");  // TypeCourse
                entity.Property(e => e.Allocation).HasColumnType("int");  // Allocation
                entity.Property(e => e.Place).HasColumnType("nvarchar(50)");  // Place
                entity.Property(e => e.RedKDist).HasColumnType("nvarchar(50)");  // RedKDist
                entity.Property(e => e.Cote).HasColumnType("real");  // Cote
                entity.Property(e => e.Avis).HasColumnType("nvarchar(255)");  // Avis
                entity.Property(e => e.Video).HasColumnType("nvarchar(255)");  // Video
                entity.Property(e => e.DateModif).HasColumnType("datetime2");  // DateModif
            });
        }
    }
}
