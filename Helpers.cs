// Copyright (c) 2019 Jennifer Messerly
// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Prerequisites;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Validation;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Localization;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace CustomMapMarkers
{
    static class ExtensionMethods
    {
        public static string StringJoin<T>(this IEnumerable<T> array, Func<T, string> map, string separator = " ") => string.Join(separator, array.Select(map));

#if DEBUG
        internal static readonly List<BlueprintScriptableObject> newAssets = new List<BlueprintScriptableObject>();
#endif
    }

    // TODO: convert everything to instance classes/methods, and subclass Helpers (renamed, of course)?
    // Benefits:
    // - less `static` noise.
    // - less `Helpers.` etc.
    internal static class Helpers
    {

        private static PropertyInfo GetPropertyInfo(Type type, string propertyName)
        {
            PropertyInfo propInfo = null;
            do
            {
                propInfo = type.GetProperty(propertyName,
                       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                type = type.BaseType;
            }
            while (propInfo == null && type != null);
            return propInfo;
        }

        public static object GetPropertyValue(this object obj, string propertyName)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");
            Type objType = obj.GetType();
            PropertyInfo propInfo = GetPropertyInfo(objType, propertyName);
            if (propInfo == null)
                throw new ArgumentOutOfRangeException("propertyName",
                  string.Format("Couldn't find property {0} in type {1}", propertyName, objType.FullName));
            return propInfo.GetValue(obj, null);
        }


        public static void SetField(object obj, string name, object value)
        {
            AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
        }

        public static void SetLocalizedStringField(BlueprintScriptableObject obj, string name, string value)
        {
            AccessTools.Field(obj.GetType(), name).SetValue(obj, Helpers.CreateString($"{obj.name}.{name}", value));
        }

        public static object GetField(object obj, string name)
        {
            return AccessTools.Field(obj.GetType(), name).GetValue(obj);
        }

        public static object GetField(Type type, object obj, string name)
        {
            return AccessTools.Field(type, name).GetValue(obj);
        }

        public static T GetField<T>(object obj, string name)
        {
            return (T)AccessTools.Field(obj.GetType(), name).GetValue(obj);
        }
        
        public static FastGetter CreateGetter<T>(string name) => CreateGetter(typeof(T), name);

        public static FastGetter CreateGetter(Type type, string name)
        {
            return new FastGetter(FastAccess.CreateGetterHandler<object, object>(AccessTools.Property(type, name)));
        }

        public static FastGetter CreateFieldGetter<T>(string name) => CreateFieldGetter(typeof(T), name);

        public static FastGetter CreateFieldGetter(Type type, string name)
        {
            return new FastGetter(FastAccess.CreateGetterHandler<object, object>(AccessTools.Field(type, name)));
        }

        public static FastSetter CreateSetter<T>(string name) => CreateSetter(typeof(T), name);

        public static FastSetter CreateSetter(Type type, string name)
        {
            return new FastSetter(FastAccess.CreateSetterHandler<object,object>(AccessTools.Property(type, name)));
        }

        public static FastSetter CreateFieldSetter<T>(string name) => CreateFieldSetter(typeof(T), name);

        public static FastSetter CreateFieldSetter(Type type, string name)
        {
            return new FastSetter(FastAccess.CreateSetterHandler<object,object>(AccessTools.Field(type, name)));
        }

        public static FastInvoke CreateInvoker<T>(String name) => CreateInvoker(typeof(T), name);

        public static FastInvoke CreateInvoker(Type type, String name)
        {
            return new FastInvoke(MethodInvoker.GetHandler(AccessTools.Method(type, name)));
        }

        public static FastInvoke CreateInvoker<T>(String name, Type[] args, Type[] typeArgs = null) => CreateInvoker(typeof(T), name, args, typeArgs);

        public static FastInvoke CreateInvoker(Type type, String name, Type[] args, Type[] typeArgs = null)
        {
            return new FastInvoke(MethodInvoker.GetHandler(AccessTools.Method(type, name, args, typeArgs)));
        }

        internal static LocalizedString CreateString(string key, string value)
        {
            // See if we used the text previously.
            // (It's common for many features to use the same localized text.
            // In that case, we reuse the old entry instead of making a new one.)
            LocalizedString localized;
            if (textToLocalizedString.TryGetValue(value, out localized))
            {
                return localized;
            }
            var strings = LocalizationManager.CurrentPack.Strings;
            String oldValue;
            if (strings.TryGetValue(key, out oldValue) && value != oldValue)
            {
                Log.Write($"Info: duplicate localized string `{key}`, different text.");
            }
            strings[key] = value;
            localized = new LocalizedString();
            localizedString_m_Key(localized, key);
            textToLocalizedString[value] = localized;
            return localized;
        }

        // All localized strings created in this mod, mapped to their localized key. Populated by CreateString.
        static Dictionary<String, LocalizedString> textToLocalizedString = new Dictionary<string, LocalizedString>();
        static FastSetter localizedString_m_Key = Helpers.CreateFieldSetter<LocalizedString>("m_Key");
    }

    internal static class Log
    {
        static readonly StringBuilder str = new StringBuilder();

        internal static void Flush()
        {
            if (str.Length == 0) return;
            Main.logger.Log(str.ToString());
            str.Clear();
        }

        [System.Diagnostics.Conditional("DEBUG")]
        internal static void Write(String message)
        {
            Append(message);
            Flush();
        }

        [System.Diagnostics.Conditional("DEBUG")]
        internal static void Append(String message)
        {
            str.AppendLine(message);
        }

        internal static void Error(Exception e)
        {
            str.AppendLine(e.ToString());
            Flush();
        }

        internal static void Error(String message)
        {
            str.AppendLine(message);
            Flush();
        }

        internal static void Append(LevelEntry level, String indent = "", bool showSelection = false)
        {
            Append($"{indent}level {level.Level}");
            foreach (var f in level.Features)
            {
                Append(f, $"{indent}  | ", showSelection);
            }
        }

        internal static void Write(BlueprintScriptableObject fact, String indent = "", bool showSelection = false)
        {
            Append(fact, indent, showSelection);
            Flush();
        }

        internal static void Append(BlueprintScriptableObject fact, String indent = "", bool showSelection = false)
        {
            if (fact == null)
            {
                Append($"{indent}BlueprintScriptableObject is null");
                return;
            }

            var @class = fact as BlueprintCharacterClass;
            var displayName = (fact as BlueprintUnitFact)?.Name ?? @class?.Name;
            Append($"{indent}fact {fact.name}, display name: \"{displayName}\", type: {fact.GetType().Name}, id {fact.AssetGuid}");
            var race = fact as BlueprintRace;
            if (race != null)
            {
                Append($"{indent} race {race.RaceId} size {race.Size} features:");
                foreach (var f in race.Features)
                {
                    Append(f, $"{indent}    ");
                }
            }
            var selection = fact as BlueprintFeatureSelection;
            if (selection != null && showSelection)
            {
                Append($"{indent}  Group {selection.Group}, Group2 {selection.Group2 + $", Mode {selection.Mode}, AllFeatures:"}");
                foreach (var g in selection.AllFeatures)
                {
                    Append(g, $"{indent}    ");
                }
            }
            var parameterized = fact as BlueprintParametrizedFeature;
            if (parameterized != null)
            {
                Append($"{indent}  param type {parameterized.ParameterType}");
                Append($"{indent}  prereq {parameterized.Prerequisite}");
                switch (parameterized.ParameterType)
                {
                    case FeatureParameterType.WeaponCategory:
                        Append($"{indent}  weapon sub category " + parameterized.WeaponSubCategory +
                            $", req proficient? {parameterized.RequireProficiency}");
                        break;
                    case FeatureParameterType.Skill:
                    case FeatureParameterType.SpellSchool:
                        break;
                    case FeatureParameterType.LearnSpell:
                        Append($"{indent}  spellcaster class {parameterized.SpellcasterClass}");
                        if (parameterized.SpecificSpellLevel)
                        {
                            Append($"{indent}  specific spell level {parameterized.SpellLevel}");
                        }
                        else
                        {
                            Append($"{indent}  spell level penalty {parameterized.SpellLevelPenalty}");
                        }
                        goto case FeatureParameterType.SpellSpecialization;
                    case FeatureParameterType.Custom:
                        Append($"{indent}  has no such feature? {parameterized.HasNoSuchFeature}");
                        goto case FeatureParameterType.SpellSpecialization;
                    case FeatureParameterType.SpellSpecialization:
                        Append($"{indent}  param variants: {parameterized.BlueprintParameterVariants.Length}");
                        break;
                }
            }
            var feature = fact as BlueprintFeature;
            if (feature != null)
            {
                foreach (var g in feature.Groups)
                {
                    Append($"{indent}  group {g}");
                }
            }
            var ability = fact as BlueprintAbility;
            if (ability != null)
            {
                Append($"{indent}  type {ability.Type} action type " + ability.ActionType +
                    $" metamagic {ability.AvailableMetamagic.ToString("x")} descriptor " + ability.SpellDescriptor.ToString("x"));
                var resources = ability.ResourceAssetIds;
                if (resources != null && resources.Length > 0)
                {
                    Append($"{indent}  resource ids: {string.Join(",", resources)}");
                }
            }
            var buff = fact as BlueprintBuff;
            if (buff != null)
            {
                Append($"{indent}buff frequency {buff.Frequency} stacking {buff.Stacking}");
            }
            if (@class != null)
            {
                Append($"{indent}progression:");
                Append(@class.Progression, $"{indent}  ");
            }
            Append($"{indent}components:");
            foreach (var c in fact.ComponentsArray)
            {
                Append(c, $"{indent}  ");
            }
            var progression = fact as BlueprintProgression;
            if (progression != null)
            {
                foreach (var c in progression.Classes)
                {
                    Append($"{indent}  class {c.name}");
                }
                //foreach (var a in progression.Archetypes)
                //{
                //    Append($"{indent}  archetype {a.name}");
                //}
                foreach (var g in progression.UIGroups)
                {
                    Append($"{indent}  ui group");
                    foreach (var f in g.Features)
                    {
                        Append($"{indent}    {f.name}");
                    }
                }
                foreach (var level in progression.LevelEntries)
                {
                    Append(level, $"{indent}  ", showSelection);
                }
            }
        }

        internal static void Append(BlueprintComponent c, String indent = "")
        {
            var spellComp = c as SpellComponent;
            if (spellComp != null)
            {
                Append($"{indent}SpellComponent school {spellComp.School.ToString()}");
                return;
            }
            var spellList = c as SpellListComponent;
            if (spellList != null)
            {
                Append($"{indent}SpellListComponent {spellList.SpellList.name} level {spellList.SpellLevel}");
                return;
            }
            var spellDesc = c as SpellDescriptorComponent;
            if (spellDesc != null)
            {
                Append($"{indent}SpellDescriptorComponent " + spellDesc.Descriptor.Value.ToString("x"));
                return;
            }
            var execOnCast = c as AbilityExecuteActionOnCast;
            if (execOnCast != null)
            {
                Append($"{indent}AbilityExecuteActionOnCast:");
                var conditions = execOnCast.Conditions;
                if (conditions != null)
                {
                    Append($"{indent}  conditions op {conditions.Operation}:");
                    foreach (var cond in conditions.Conditions)
                    {
                        Append($"{indent}  " + (cond.Not ? "not " : " ") + c.GetType().Name + $" {cond.GetCaption()}");
                    }
                }
                Append($"{indent}  actions:");
                foreach (var action in execOnCast.Actions.Actions)
                {
                    Append(action, $"{indent}    ");
                }
            }
            var runActions = c as AbilityEffectRunAction;
            if (runActions != null)
            {
                Append($"{indent}AbilityEffectRunAction saving throw {runActions.SavingThrowType}");
                foreach (var action in runActions.Actions.Actions)
                {
                    Append(action, $"{indent}  ");
                }
                return;
            }
            var config = c as ContextRankConfig;
            if (config != null)
            {
                Func<String, object> field = (name) => Helpers.GetField(config, name);

                var progression = (ContextRankProgression)field("m_Progression");
                Append($"{indent}ContextRankConfig type {config.Type} value type " +
                    ((ContextRankBaseValueType)field("m_BaseValueType")) + $" progression {progression}");

                if (config.IsBasedOnClassLevel)
                {
                    Append($"{indent}  class level " + ((BlueprintCharacterClass[])field("m_Class"))?.StringJoin(b => b.name));
                    Append($"{indent}  except classes? " + (bool)field("m_ExceptClasses"));
                }
                if (config.RequiresArchetype) Append($"{indent}  archetype " + ((BlueprintArchetype)field("Archetype")).name);
                if (config.IsBasedOnFeatureRank) Append($"{indent}  feature rank " + ((BlueprintFeature)field("m_Feature")).name);
                if (config.IsFeatureList) Append($"{indent}  feature list " + ((BlueprintFeature[])field("m_FeatureList"))?.StringJoin(f => f.name));
                if (config.IsBasedOnStatBonus) Append($"{indent}  stat bonus " + ((StatType)field("m_Stat")));
                if ((bool)field("m_UseMax")) Append($"{indent}  max " + ((int)field("m_Max")));
                if ((bool)field("m_UseMin")) Append($"{indent}  min " + ((int)field("m_Min")));
                if (config.IsDivisionProgression) Append($"{indent}  start level " + ((int)field("m_StartLevel")));
                if (config.IsDivisionProgressionStart) Append($"{indent}  step level " + ((int)field("m_StepLevel")));
                if (progression == ContextRankProgression.Custom)
                {
                    Append($"{indent}  custom progression:");
                    foreach (var p in (IEnumerable<object>)field("m_CustomProgression"))
                    {
                        Func<String, int> field2 = (name) => (int)Helpers.GetField(p, name);
                        Append($"{indent}    base value {field2("BaseValue")} progression {field2("ProgressionValue")}");
                    }
                }
                return;
            }

            Append($"{indent}component {c.name}, type {c.GetType().Name}");

            var abilityVariants = c as AbilityVariants;
            if (abilityVariants != null)
            {
                foreach (var v in abilityVariants.Variants)
                {
                    Append(v, $"{indent}    ");
                }
            }
            var polymorph = c as Polymorph;
            if (polymorph != null)
            {
                foreach (var f in polymorph.Facts)
                {
                    Append(f, $"{indent}    ");
                }
            }
            var stickyTouch = c as AbilityEffectStickyTouch;
            if (stickyTouch != null)
            {
                Append(stickyTouch.TouchDeliveryAbility, $"{indent}  ");
            }
            var addIf = c as AddFeatureIfHasFact;
            if (addIf != null)
            {
                Append($"{indent}  if {(addIf.Not ? "!" : "")}{addIf.CheckedFact.name} then add {addIf.Feature.name}");
                Append(addIf.Feature, $"{indent}    ");
            }
            var addOnLevel = c as AddFeatureOnClassLevel;
            if (addOnLevel != null)
            {
                Append($"{indent}  if level {(addOnLevel.BeforeThisLevel ? "before " : "")}{addOnLevel.Level} then add {addOnLevel.Feature.name}");
                Append(addOnLevel.Feature, $"{indent}    ");
            }
            var addFacts = c as AddFacts;
            if (addFacts != null)
            {
                Append($"{indent}  add facts");
                foreach (var f in addFacts.Facts)
                {
                    Append(f, $"{indent}    ");
                }
            }
            var prereq = c as Prerequisite;
            if (prereq != null)
            {
                Append($"{indent}  prerequisite group {prereq.Group}");

                var log = new StringBuilder();
                Action<String, object> logIf = (desc, name) =>
                {
                    if (name != null) log.Append(desc + name);
                };
                logIf(" class ", (prereq as PrerequisiteClassLevel)?.CharacterClass.name);
                logIf(" level ", (prereq as PrerequisiteClassLevel)?.Level);
                logIf(" feature ", (prereq as PrerequisiteFeature)?.Feature.name);
                logIf(" no feature ", (prereq as PrerequisiteNoFeature)?.Feature.name);
                logIf(" stat ", (prereq as PrerequisiteStatValue)?.Stat.ToString());
                logIf(" value ", (prereq as PrerequisiteStatValue)?.Value);
                Append($"{indent}   {log.ToString()}");
            }
        }

        internal static void Append(GameAction action, String indent = "")
        {
            try
            {
                if (action == null)
                {
                    Append($"{indent}GameAction is null");
                    return;
                }

                {
                    Append(indent + action.GetType().Name + $" {action.GetCaption()}");
                }
                {
                    var a = action as Conditional;
                    if (a != null)
                    {
                        Append($"{indent}  operation {a.ConditionsChecker?.Operation}");
                        var conditions = a.ConditionsChecker?.Conditions;
                        if (conditions != null)
                        {
                            foreach (var c in conditions)
                            {
                                Append($"{indent}  {(c.Not ? "not " : " ")}{c.GetType().Name} {c.GetCaption()}");
                            }
                        }
                        if (a.IfTrue?.HasActions == true)
                        {
                            Append($"{indent}  if true:");
                            foreach (var nested in a.IfTrue.Actions)
                            {
                                Append(nested, $"{indent}   ");
                            }
                        }
                        if (a.IfFalse?.HasActions == true)
                        {
                            Append($"{indent}  if false:");
                            foreach (var nested in a.IfFalse.Actions)
                            {
                                Append(nested, $"{indent}   ");
                            }
                        }
                    }
                }
                {
                    var a = action as ContextActionConditionalSaved;
                    if (a != null)
                    {
                        Append($"{indent}  succeeded:");
                        if (a.Succeed?.HasActions == true)
                        {
                            foreach (var nested in a.Succeed.Actions ?? Array.Empty<GameAction>())
                            {
                                Append(nested, $"{indent}    ");
                            }
                        }
                        if (a.Failed?.HasActions == true)
                        {
                            Append($"{indent}  failed:");
                            foreach (var nested in a.Failed.Actions ?? Array.Empty<GameAction>())
                            {
                                Append(nested, $"{indent}    ");
                            }
                        }
                    }
                }
                {
                    var a = action as ContextActionSavingThrow;
                    if (a != null)
                    {
                        foreach (var nested in a.Actions.Actions)
                        {
                            Append(nested, $"{indent}  ");
                        }
                    }
                }
                {
                    var a = action as ContextActionApplyBuff;
                    if (a != null)
                    {
                        Append(a.Buff, $"{indent}  ");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"Error evaluating {action?.GetType()}:\n{e}");
            }
        }

        /*
        [System.Diagnostics.Conditional("DEBUG")]
        internal static void Validate(BlueprintComponent c, BlueprintScriptableObject parent)
        {
            c.Validate(validation);
            if (validation.HasErrors)
            {
                Append($"Error: component `{c.name}` of `{parent.name}` failed validation:");
                foreach (var e in validation.Errors) Append($"  {e}");
                ((List<ValidationContext.Error>)validation.ErrorsAdvanced).Clear();
                Flush();
            }
        }
        */

        internal static void MaybeFlush()
        {
            if (str.Length > 4096) Flush();
        }

        static ValidationContext validation = new ValidationContext();
    }

    public delegate void FastSetter(object source, object value);
    public delegate object FastGetter(object source);
    public delegate void FastSetter<T,F>(F source, T value);
    public delegate object FastGetter<T, F>(F source);
    public delegate object FastInvoke(object target, params object[] paramters);
}
