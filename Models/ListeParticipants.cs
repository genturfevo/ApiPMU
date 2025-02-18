using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ApiPMU.Models
{
    public class ListeParticipants
    {
        public virtual ICollection<Cheval> Chevaux { get; set; } = new List<Cheval>();
        public virtual ICollection<Performance> Performances { get; set; } = new List<Performance>();
        public virtual ICollection<EntraineurJokey> EntraineurJokeys { get; set; } = new List<EntraineurJokey>();
    }
}
