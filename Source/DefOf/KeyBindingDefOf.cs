using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace SolarWeb.Stratum;

    [RimWorld.DefOf]
    public static class KeyBindingDefOf
    {
        public static KeyBindingDef ToggleRoofVisibility;

        static KeyBindingDefOf()
        {
            ToggleRoofVisibility = new KeyBindingDef();

            DefOfHelper.EnsureInitializedInCtor(typeof(KeyBindingDefOf));
        }
    }
