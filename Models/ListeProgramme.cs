using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ApiPMU.Models
{
    public class ListeProgramme
    {
        public virtual ICollection<Reunion> Reunions { get; set; } = new List<Reunion>();
        public virtual ICollection<Course> Courses { get; set; } = new List<Course>();
    }
}
