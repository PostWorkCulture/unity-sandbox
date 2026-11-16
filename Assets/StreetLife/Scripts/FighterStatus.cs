// What a fighter still carries now that character abilities are out of the game (16 Nov): the stun timer
// (Rumble's air strike, ScreenFx's stun stars) and which character it wears.
using System;
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter
    {
        /// seconds of stun left: little steering while it runs
        [NonSerialized] public float stunT;

        CharacterRig statusRig; bool statusRigLooked;
        /// the web game's character id this fighter is wearing (wednesday, blaze, ...)
        public string CharId
        {
            get
            {
                if (!statusRigLooked) { statusRig = GetComponent<CharacterRig>(); statusRigLooked = true; }
                var id = statusRig ? CharacterSkin.Of(statusRig) : null;
                if (!string.IsNullOrEmpty(id)) return id;
                var set = CharacterSet.Load();
                if (set) foreach (var c in set.All) if (c.name == characterName) return c.id;
                return "wednesday";
            }
        }
        public CharacterSet.Info CharInfo { get { var set = CharacterSet.Load(); return set ? set.Get(CharId) : null; } }

        void ResetStatus() { stunT = 0; }

        void TickStatus(float dt)
        {
            if (!alive) stunT = 0;
            else if (stunT > 0) stunT -= dt;
        }
    }
}
