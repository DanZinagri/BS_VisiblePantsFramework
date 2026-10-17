using System.Collections.Generic;
using Verse;

namespace BSVisiblePantsFramework
{
    // One lower-body type that gets its own visible pants list in the settings.
    // A pawn counts as this body when it has an active gene that Big and Small swaps to one of
    // raceSwaps (found on every loaded gene at startup, from any mod), or one of the genes listed
    // by name. Replacements give a listed apparel item a different worn texture on this body.
    // Gene, race and apparel names are plain strings, so entries for mods that aren't loaded
    // simply never match instead of raising config errors.
    public class BodyHideDef : Def
    {
        public List<string> raceSwaps = new List<string>();
        public List<string> genes = new List<string>();
        public List<ApparelReplacement> replacements = new List<ApparelReplacement>();
    }

    public class ApparelReplacement
    {
        public string apparel;

        // Same convention as a vanilla wornGraphicPath: path_<BodyType>_south is tried first,
        // then path_south. Mask files (path_southm) switch the swap to the masked shader.
        public string wornGraphicPath;
    }
}
