namespace ApiPMU.Models
{
    /// <summary>
    /// Conteneur regroupant les réunions et les courses issues du parsing du JSON.
    /// </summary>
    public class Programme
    {
        public List<Reunion> Reunions { get; set; } = new List<Reunion>();
        public List<Course> Courses { get; set; } = new List<Course>();
    }
}
