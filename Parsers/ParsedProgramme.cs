using System.Collections.Generic;
using ApiPMU.Models;

namespace ApiPMU.Parsers
{
    /// <summary>
    /// Contient la première réunion extraite du JSON et la liste des courses associées.
    /// </summary>
    public class ParsedProgramme
    {
        public Reunion Reunion { get; set; }
        public List<Course> Courses { get; set; }
    }
}
