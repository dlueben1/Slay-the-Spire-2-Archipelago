using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Models
{
    public class CharacterConfig
    {
        public String Name { get; set; }
        public String OptionName { get; set; }
        public int CharOffset { get; set; }
        public String OfficialName { get; set; }
        public String Seed { get; set; }
        public bool Locked { get; set; } = false;
        public int ModNum { get; set; }
        public ISet<String> Ascension { get; set; } = new HashSet<String>();

    }
}
