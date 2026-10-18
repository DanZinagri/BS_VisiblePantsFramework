using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BSVisiblePantsFramework
{
    // One apparel item in one body's list.
    public class ItemEntry
    {
        public BodyEntry body;
        public ThingDef apparel;

        // Settings key, "<body defName>|<apparel defName>".
        public string key;

        // Null when the body has no replacement for this item, or its texture isn't loaded.
        public string replacementPath;

        // Settings folded in: drawn (original or replacement) or hidden.
        public bool shown;

        // Body type -> resolved texture path (null = no texture for that body type), and whether
        // that path ships masks. Filled lazily, never cleared: textures don't change in a session.
        private readonly Dictionary<BodyTypeDef, string> resolvedPaths = new Dictionary<BodyTypeDef, string>();
        private readonly Dictionary<BodyTypeDef, bool> resolvedMasks = new Dictionary<BodyTypeDef, bool>();

        public bool HasReplacement => replacementPath != null;

        public string ReplacementPathFor(BodyTypeDef bodyType, out bool masked)
        {
            if (!resolvedPaths.TryGetValue(bodyType, out string path))
            {
                path = replacementPath + "_" + bodyType.defName;
                if (!TextureExists(path))
                {
                    path = TextureExists(replacementPath) ? replacementPath : null;
                }
                resolvedPaths[bodyType] = path;
                resolvedMasks[bodyType] = path != null
                    && ContentFinder<Texture2D>.Get(path + "_southm", reportFailure: false) != null;
            }
            masked = resolvedMasks[bodyType];
            return path;
        }

        public static bool TextureExists(string path)
        {
            return ContentFinder<Texture2D>.Get(path + "_south", reportFailure: false) != null;
        }
    }

    public class BodyEntry
    {
        public BodyHideDef def;

        // Sorted by label, for the settings list.
        public List<ItemEntry> items = new List<ItemEntry>();
        public Dictionary<ThingDef, ItemEntry> byApparel = new Dictionary<ThingDef, ItemEntry>();
        public List<GeneDef> genes = new List<GeneDef>();

        // Settings-window state only, not saved.
        public bool expanded;
    }

    // Everything the graphic patch needs, resolved once at startup: which gene puts a pawn in which
    // body, and per body which apparel is listed and what it draws. A lookup is then a walk over
    // the wearer's genes with dictionary hits and no allocation, and it only happens when a pawn's
    // render tree is rebuilt, never per frame.
    [StaticConstructorOnStartup]
    public static class BodyRegistry
    {
        public static readonly List<BodyEntry> Bodies = new List<BodyEntry>();

        private static readonly Dictionary<GeneDef, BodyEntry> geneToBody = new Dictionary<GeneDef, BodyEntry>();

        static BodyRegistry()
        {
            foreach (BodyHideDef def in DefDatabase<BodyHideDef>.AllDefsListForReading)
            {
                Bodies.Add(new BodyEntry { def = def });
            }
            MapGenes();

            // A body no loaded gene can grant (its content mod, usually Genes & More, is missing)
            // can never match a pawn, so it's left out of the settings entirely.
            Bodies.RemoveAll(b => b.genes.Count == 0);

            BuildItems();
            Refresh();
        }

        public static BodyEntry BodyOf(Pawn pawn)
        {
            if (geneToBody.Count == 0 || pawn.genes == null)
            {
                return null;
            }
            List<Gene> genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                if (geneToBody.TryGetValue(genes[i].def, out BodyEntry body) && genes[i].Active)
                {
                    return body;
                }
            }
            return null;
        }

        // Re-reads every item's shown state from the settings.
        public static void Refresh()
        {
            foreach (BodyEntry body in Bodies)
            {
                foreach (ItemEntry item in body.items)
                {
                    item.shown = BSVPFMod.Settings.IsShown(item);
                }
            }
        }

        // Rebuilds the render tree of every pawn the settings can affect, so a change shows as soon
        // as the settings window closes. SetAllGraphicsDirty is a no-op for pawns never drawn.
        public static void RedrawAffectedPawns()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                if (pawn.RaceProps.Humanlike && BodyOf(pawn) != null)
                {
                    pawn.Drawer.renderer.SetAllGraphicsDirty();
                }
            }
        }

        // Named genes first, then every gene whose Big and Small PawnExtension swaps the pawn to
        // one of a body's races. A gene already claimed keeps its first body (Marilith carries two
        // swaps: the naga body and six arms). Big and Small is read through reflection rather than
        // compiled against: it has no 1.7 build, and its 1.6 DLL's Unity signatures don't compile
        // under net10.
        private static void MapGenes()
        {
            foreach (BodyEntry body in Bodies)
            {
                foreach (string name in body.def.genes)
                {
                    GeneDef gene = DefDatabase<GeneDef>.GetNamedSilentFail(name);
                    if (gene != null)
                    {
                        Claim(gene, body);
                    }
                }
            }

            Type extType = AccessTools.TypeByName("BigAndSmall.PawnExtension");
            FieldInfo swapField = extType == null ? null : AccessTools.Field(extType, "thingDefSwap");
            if (swapField == null)
            {
                return;
            }
            Dictionary<string, BodyEntry> raceToBody = new Dictionary<string, BodyEntry>();
            foreach (BodyEntry body in Bodies)
            {
                foreach (string race in body.def.raceSwaps)
                {
                    if (!raceToBody.ContainsKey(race))
                    {
                        raceToBody[race] = body;
                    }
                }
            }
            foreach (GeneDef gene in DefDatabase<GeneDef>.AllDefsListForReading)
            {
                if (gene.modExtensions == null)
                {
                    continue;
                }
                foreach (DefModExtension ext in gene.modExtensions)
                {
                    if (extType.IsInstanceOfType(ext)
                        && swapField.GetValue(ext) is ThingDef race
                        && raceToBody.TryGetValue(race.defName, out BodyEntry body))
                    {
                        Claim(gene, body);
                        break;
                    }
                }
            }
        }

        private static void Claim(GeneDef gene, BodyEntry body)
        {
            if (!geneToBody.ContainsKey(gene))
            {
                geneToBody[gene] = body;
                body.genes.Add(gene);
            }
        }

        // Every body lists all drawn legwear, plus anything its replacements name. A replacement
        // whose texture isn't loaded (its art mod is missing) is dropped with a warning, so
        // "Has Replacement" never promises art that can't be drawn.
        private static void BuildItems()
        {
            List<ThingDef> legwear = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsDrawnLegwear)
                .ToList();

            foreach (BodyEntry body in Bodies)
            {
                Dictionary<ThingDef, string> paths = new Dictionary<ThingDef, string>();
                foreach (ApparelReplacement rep in body.def.replacements)
                {
                    ThingDef apparel = DefDatabase<ThingDef>.GetNamedSilentFail(rep.apparel);
                    if (apparel == null || !apparel.IsApparel || rep.wornGraphicPath.NullOrEmpty() || paths.ContainsKey(apparel))
                    {
                        continue;
                    }
                    if (!AnyTextureFor(rep.wornGraphicPath))
                    {
                        Log.Warning($"[Big and Small - Visible Pants Framework] {body.def.defName}: no texture found at {rep.wornGraphicPath} for {apparel.defName}; listed without a replacement.");
                        continue;
                    }
                    paths[apparel] = rep.wornGraphicPath;
                }

                foreach (ThingDef apparel in legwear.Concat(paths.Keys).Distinct())
                {
                    ItemEntry item = new ItemEntry
                    {
                        body = body,
                        apparel = apparel,
                        key = body.def.defName + "|" + apparel.defName,
                        replacementPath = paths.TryGetValue(apparel, out string path) ? path : null,
                    };
                    body.items.Add(item);
                    body.byApparel[apparel] = item;
                }
                body.items.SortBy(i => i.apparel.label ?? i.apparel.defName);
            }
        }

        // Anything Visible Pants marked, or any apparel with a worn texture that covers the legs but
        // not the torso. Addons such as Medieval Visible Pants Addon only add a wornGraphicPath and
        // never Visible Pants' marker, so the marker alone misses them. Torso coverage rules out
        // dresses, robes and full armour, which aren't legwear.
        private static bool IsDrawnLegwear(ThingDef def)
        {
            if (!def.IsApparel)
            {
                return false;
            }
            if (def.HasModExtension<VisiblePants.ThingDefExtension>())
            {
                return true;
            }
            List<BodyPartGroupDef> groups = def.apparel.bodyPartGroups;
            return !def.apparel.wornGraphicPath.NullOrEmpty()
                && groups.Contains(BodyPartGroupDefOf.Legs)
                && !groups.Contains(BodyPartGroupDefOf.Torso);
        }

        private static bool AnyTextureFor(string path)
        {
            if (ItemEntry.TextureExists(path))
            {
                return true;
            }
            foreach (BodyTypeDef bodyType in DefDatabase<BodyTypeDef>.AllDefsListForReading)
            {
                if (ItemEntry.TextureExists(path + "_" + bodyType.defName))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
