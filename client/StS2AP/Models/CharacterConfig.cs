using Newtonsoft.Json.Linq;
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

        public CharacterConfig() {  }

        public static CharacterConfig? fromJObject(JObject charObj)
        {

            CharacterConfig config = new CharacterConfig();
            if(charObj.TryGetValue("name", out var name))
            {
                config.Name = name.ToString();
            }
            else
            {
                return null;
            }

            if(charObj.TryGetValue("option_name", out var optionName))
            {
                config.OptionName = optionName.ToString();
            }
            else
            {
                return null;
            }
            if(charObj.TryGetValue("char_offset", out var offset))
            {
                config.CharOffset = ((int)offset);
            }
            else
            {
                return null;
            }
            if(charObj.TryGetValue("official_name", out var official_name))
            {
                config.OfficialName = official_name.ToString();
            }
            else
            {
                return null;
            }
            if(charObj.TryGetValue("seed", out var seed))
            {
                config.Seed = seed.ToString();
            }
            if(charObj.TryGetValue("locked", out var locked))
            {
                config.Locked = (bool)locked;
            }
            if(charObj.TryGetValue("mod_num", out var modNum))
            {
                config.ModNum = (int) modNum;
            }

            if(charObj.TryGetValue("ascension", out var ascension))
            {
                config.Ascension = ascension.ToObject<HashSet<String>>();
            }
            return config;
        }

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
