using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Models
{
    /// <summary>
    /// The slot data configuration for a single character.
    /// </summary>
    public class CharacterConfig
    {
        /// <summary>
        /// The human readable name for the character
        /// </summary>
        public String Name { get; set; }
        /// <summary>
        /// The option name in the yaml for the character
        /// </summary>
        public String OptionName { get; set; }
        /// <summary>
        /// The offset for item and location ids for this character
        /// </summary>
        public int CharOffset { get; set; }
        /// <summary>
        /// The name to do a lookup in the mod for the character
        /// </summary>
        public String OfficialName { get; set; }
        /// <summary>
        /// The seed to run with for this character, if it was set
        /// </summary>
        public String? Seed { get; set; }
        /// <summary>
        /// Whether this character should start locked.
        /// </summary>
        public bool Locked { get; set; } = false;
        /// <summary>
        /// If this is a modded character, the index of the character in the options in the yaml
        /// </summary>
        public int ModNum { get; set; }
        /// <summary>
        /// The starting ascension configuration for the character.
        /// </summary>
        public ISet<String> Ascension { get; set; } = new HashSet<String>();

    }
}
