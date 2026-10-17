using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BSVisiblePantsFramework
{
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            new Harmony("DanZinagri.BSVisiblePantsFramework").PatchAll();
        }
    }

    // Decides listed apparel's worn graphic on pawns with a listed body: the original (run the
    // getter as normal), a replacement texture, or nothing at all. Hiding returns false with an
    // empty record, which is exactly what the getter does for any apparel without a worn texture,
    // so PawnRenderNode_Apparel.GraphicsFor draws nothing and every other mod's postfix on this
    // getter already handles it. PawnRenderNode_Apparel.GraphicsFor itself is deliberately not
    // patched: on this modlist its patch list has held an unresolvable method token before.
    [HarmonyPatch(typeof(ApparelGraphicRecordGetter), nameof(ApparelGraphicRecordGetter.TryGetGraphicApparel))]
    public static class ApparelGraphicRecordGetter_TryGetGraphicApparel_Patch
    {
        public static bool Prefix(Apparel apparel, BodyTypeDef bodyType, bool forStatue, ref ApparelGraphicRecord rec, ref bool __result)
        {
            if (!BSVPFMod.Settings.enabled || apparel?.Wearer == null)
            {
                return true;
            }
            BodyEntry body = BodyRegistry.BodyOf(apparel.Wearer);
            if (body == null || !body.byApparel.TryGetValue(apparel.def, out ItemEntry item))
            {
                return true;
            }
            if (item.shown && !item.HasReplacement)
            {
                return true;
            }

            // A replacement with no texture for this body type hides rather than falling back to
            // the original, since the original is what looks wrong on this body.
            bool masked = false;
            string path = item.shown ? item.ReplacementPathFor(bodyType ?? BodyTypeDefOf.Male, out masked) : null;
            if (path == null)
            {
                rec = new ApparelGraphicRecord(null, null);
                __result = false;
                return false;
            }

            Shader shader = ShaderDatabase.Cutout;
            if (!forStatue)
            {
                if (apparel.StyleDef?.graphicData.shaderType != null)
                {
                    shader = apparel.StyleDef.graphicData.shaderType.Shader;
                }
                else if (masked
                    || (apparel.StyleDef == null && apparel.def.apparel.useWornGraphicMask)
                    || (apparel.StyleDef != null && apparel.StyleDef.UseWornGraphicMask))
                {
                    shader = ShaderDatabase.CutoutComplex;
                }
            }
            rec = new ApparelGraphicRecord(GraphicDatabase.Get<Graphic_Multi>(path, shader, apparel.def.graphicData.drawSize, apparel.DrawColor), apparel);
            __result = true;
            return false;
        }
    }
}
