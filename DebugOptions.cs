namespace ApiPMU
{
    /// <summary>
    /// Options de débogage pour forcer l'exécution du téléchargement pour une date donnée.
    /// Format attendu : "ddMMyyyy" (par exemple, "25022025")
    /// </summary>
    public class DebugOptions
    {
        public string? ForcedDate { get; set; }
    }
}
