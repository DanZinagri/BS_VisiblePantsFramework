using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace BSVisiblePantsFramework
{
    public class BSVPFSettings : ModSettings
    {
        public bool enabled = true;

        // Only the choices that differ from the default, keyed "<body defName>|<apparel defName>".
        // The default is shown when the item has a replacement and hidden otherwise, so apparel or
        // replacements added later start out sensible without touching this file.
        public Dictionary<string, bool> overrides = new Dictionary<string, bool>();

        public bool IsShown(ItemEntry item)
        {
            return overrides.TryGetValue(item.key, out bool shown) ? shown : item.HasReplacement;
        }

        public void SetShown(ItemEntry item, bool shown)
        {
            if (shown == item.HasReplacement)
            {
                overrides.Remove(item.key);
            }
            else
            {
                overrides[item.key] = shown;
            }
            item.shown = shown;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Collections.Look(ref overrides, "overrides", LookMode.Value, LookMode.Value);
            overrides ??= new Dictionary<string, bool>();
        }
    }

    public class BSVPFMod : Mod
    {
        public static BSVPFSettings Settings;

        private Vector2 scroll;

        private const float HeaderHeight = 32f;
        private const float ItemHeight = 40f;
        private const float Indent = 36f;

        public BSVPFMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BSVPFSettings>();
        }

        public override string SettingsCategory() => "Big and Small - Visible Pants Framework";

        // Called when the settings window closes: fold the choices in and redraw affected pawns.
        public override void WriteSettings()
        {
            base.WriteSettings();
            BodyRegistry.Refresh();
            BodyRegistry.RedrawAffectedPawns();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard top = new Listing_Standard();
            top.Begin(inRect);
            top.CheckboxLabeled("BSVPF_Enabled".Translate(), ref Settings.enabled, "BSVPF_EnabledDesc".Translate());
            GUI.color = Color.gray;
            top.Label("BSVPF_Explanation".Translate());
            GUI.color = Color.white;
            top.Gap(4f);
            float buttonsBottom = DrawButtons(new Rect(0f, top.CurHeight, inRect.width, 0f));
            top.End();

            Rect listRect = new Rect(inRect.x, inRect.y + buttonsBottom + 8f, inRect.width, inRect.height - buttonsBottom - 8f);
            DrawList(listRect);
        }

        // Lays the bulk buttons out left to right, wrapping when they don't fit. Returns the bottom edge.
        private float DrawButtons(Rect area)
        {
            List<BodyEntry> bodies = BodyRegistry.Bodies;
            (string label, System.Action action)[] buttons =
            {
                ("BSVPF_ExpandAll".Translate(), () => bodies.ForEach(b => b.expanded = true)),
                ("BSVPF_CollapseAll".Translate(), () => bodies.ForEach(b => b.expanded = false)),
                ("BSVPF_EnableAll".Translate(), () => SetAll(bodies.SelectMany(b => b.items), true)),
                ("BSVPF_DisableAll".Translate(), () => SetAll(bodies.SelectMany(b => b.items), false)),
                ("BSVPF_DisableAllWithoutReplacements".Translate(), () => SetAll(bodies.SelectMany(b => b.items).Where(i => !i.HasReplacement), false)),
            };

            Text.Font = GameFont.Small;
            float x = area.x;
            float y = area.y;
            foreach ((string label, System.Action action) in buttons)
            {
                float width = Text.CalcSize(label).x + 24f;
                if (x > area.x && x + width > area.xMax)
                {
                    x = area.x;
                    y += 34f;
                }
                if (Widgets.ButtonText(new Rect(x, y, width, 30f), label))
                {
                    action();
                }
                x += width + 6f;
            }
            return y + 30f;
        }

        private void DrawList(Rect outRect)
        {
            List<BodyEntry> bodies = BodyRegistry.Bodies;
            if (bodies.Count == 0)
            {
                Widgets.Label(outRect, "BSVPF_NoBodies".Translate());
                return;
            }

            float height = bodies.Sum(b => HeaderHeight + (b.expanded ? b.items.Count * ItemHeight : 0f));
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, height);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            if (!Settings.enabled)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
            }

            float y = 0f;
            foreach (BodyEntry body in bodies)
            {
                DrawHeader(new Rect(0f, y, viewRect.width, HeaderHeight), body);
                y += HeaderHeight;
                if (!body.expanded)
                {
                    continue;
                }
                for (int i = 0; i < body.items.Count; i++)
                {
                    DrawItem(new Rect(Indent, y, viewRect.width - Indent, ItemHeight), body.items[i], i);
                    y += ItemHeight;
                }
            }

            GUI.color = Color.white;
            Widgets.EndScrollView();
        }

        private void DrawHeader(Rect rect, BodyEntry body)
        {
            Widgets.DrawLightHighlight(rect);
            Widgets.DrawHighlightIfMouseover(rect);

            Rect arrowRect = new Rect(rect.x + 4f, rect.y + (rect.height - 20f) / 2f, 20f, 20f);
            if (Widgets.ButtonImage(arrowRect, body.expanded ? TexButton.Collapse : TexButton.Reveal))
            {
                body.expanded = !body.expanded;
            }

            int shown = body.items.Count(i => i.shown);
            MultiCheckboxState state = shown == body.items.Count ? MultiCheckboxState.On
                : shown == 0 ? MultiCheckboxState.Off
                : MultiCheckboxState.Partial;
            Rect boxRect = new Rect(arrowRect.xMax + 6f, rect.y + (rect.height - 24f) / 2f, 24f, 24f);
            MultiCheckboxState newState = Widgets.CheckboxMulti(boxRect, state);
            if (newState != state)
            {
                SetAll(body.items, newState == MultiCheckboxState.On);
            }

            Rect labelRect = new Rect(boxRect.xMax + 8f, rect.y, rect.xMax - boxRect.xMax - 8f, rect.height);
            string note = "BSVPF_ShownCount".Translate(shown, body.items.Count).ToString();
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, body.def.LabelCap + "   <color=#999999>" + note + "</color>");
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(labelRect))
            {
                body.expanded = !body.expanded;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(labelRect, "BSVPF_GenesTip".Translate() + "\n" + string.Join("\n", body.genes.Select(g => " - " + g.LabelCap)));
        }

        private void DrawItem(Rect rect, ItemEntry item, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawAltRect(rect);
            }
            Widgets.DrawHighlightIfMouseover(rect);

            Rect iconRect = new Rect(rect.x + 4f, rect.y + 4f, 32f, 32f);
            Widgets.DefIcon(iconRect, item.apparel);

            Rect labelRect = new Rect(iconRect.xMax + 8f, rect.y, rect.width - iconRect.width - 52f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            if (item.HasReplacement)
            {
                Widgets.Label(labelRect.TopHalf(), item.apparel.LabelCap);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.55f, 0.85f, 0.55f, GUI.color.a);
                Widgets.Label(labelRect.BottomHalf(), "BSVPF_HasReplacement".Translate());
                GUI.color = new Color(1f, 1f, 1f, GUI.color.a);
                Text.Font = GameFont.Small;
            }
            else
            {
                Widgets.Label(labelRect, item.apparel.LabelCap);
            }
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, item.apparel.defName + (item.HasReplacement ? "\n" + item.replacementPath : ""));

            bool shown = item.shown;
            Widgets.Checkbox(rect.xMax - 32f, rect.y + (rect.height - 24f) / 2f, ref shown, 24f, paintable: true);
            if (shown != item.shown)
            {
                Settings.SetShown(item, shown);
            }
        }

        private static void SetAll(IEnumerable<ItemEntry> items, bool shown)
        {
            foreach (ItemEntry item in items)
            {
                Settings.SetShown(item, shown);
            }
        }
    }
}
