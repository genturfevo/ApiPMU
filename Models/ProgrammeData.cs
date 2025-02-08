using System;
using System.Collections.Generic;

namespace ApiPMU.Models
{
    /// <summary>
    /// Conteneur des résultats du parsing : listes de réunions et de courses.
    /// </summary>
    public class ProgrammeData
    {
        public List<Reunion> Reunions { get; set; } = new List<Reunion>();
        public List<Course> Courses { get; set; } = new List<Course>();
    }

    public class Reunion
    {
        public int? NumGeny { get; set; }
        public int? NumReunion { get; set; }
        public string LieuCourse { get; set; } = string.Empty;
        public DateTime DateReunion { get; set; }
        public DateTime DateModif { get; set; }
    }

    public class Course
    {
        public int? NumGeny { get; set; }
        public int? NumCourse { get; set; }
        public string Discipline { get; set; } = string.Empty;
        public string Jcouples { get; set; } = string.Empty;
        public string Jtrio { get; set; } = string.Empty;
        public string Jmulti { get; set; } = string.Empty;
        public string Jquinte { get; set; } = string.Empty;
        public bool? Autostart { get; set; }
        public string TypeCourse { get; set; } = string.Empty;
        public string Cordage { get; set; } = string.Empty;
        public string Allocation { get; set; } = string.Empty;
        public int? Distance { get; set; }
        public short? Partants { get; set; }
        public string Libelle { get; set; } = string.Empty;
        public DateTime DateModif { get; set; }
    }
}
