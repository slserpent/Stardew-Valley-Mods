using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.Enchantments;
using StardewValley.GameData.Objects;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValley.Tools;

namespace StardewWeaponStatsMod {
    internal sealed class ModEntry : Mod {
        //need a static reference for the SMAPI monitor so that the static methods for harmony patches can access it
        internal static IMonitor ModMonitor = null!;

        internal struct WeaponStats {
            public float AvgDamage;
            public float DPS;
            public int Speed; //in ms
            public float CritChance; //as 0..1
            public float CritMultiplier;
            public float Knockback;
        }

        public override void Entry(IModHelper helper) {
            var harmony = new Harmony(this.ModManifest.UniqueID);
            ModMonitor = this.Monitor;

            //patch the tooltip content draw for weapons
            //this method actually draws the content for the tooltips
            harmony.Patch(
               original: AccessTools.Method(typeof(MeleeWeapon), nameof(StardewValley.Tools.MeleeWeapon.drawTooltip)),
               prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.drawTooltip_Prefix))
            );

            //patch the tooltip content measure for weapons
            //this method gets the required size of the tooltip contents so it can draw the tooltip box to fit
            harmony.Patch(
               original: AccessTools.Method(typeof(MeleeWeapon), nameof(StardewValley.Tools.MeleeWeapon.getExtraSpaceNeededForTooltipSpecialIcons)),
               prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.getExtraSpace_Prefix))
            );
        }

        public static bool drawTooltip_Prefix(MeleeWeapon __instance, SpriteBatch spriteBatch, ref int x, ref int y, SpriteFont font, float alpha) {
            try {
                WeaponStats ModWeaponStats = GetWeaponStats(__instance);
                int LeftMargin = x + 16;
                int TextLeftMargin = 52;
                var EnchantmentColor = new Color(0, 120, 120);
                //this is how we get access to methods in this instance's base class
                var ModGetDescriptionWidth = AccessTools.Method(__instance.GetType().BaseType, "getDescriptionWidth");

                //DRAW DESCRIPTION TEXT
                Utility.drawTextWithShadow(spriteBatch, Game1.parseText(__instance.description, Game1.smallFont, (int)ModGetDescriptionWidth.Invoke(__instance, null)!), font, new Vector2(LeftMargin, y + 16 + 4), Game1.textColor);
                //measure the height of the text so we can increment y as we go
                y += (int)font.MeasureString(Game1.parseText(__instance.description, Game1.smallFont, (int)ModGetDescriptionWidth.Invoke(__instance, null)!)).Y;

                //scythe doesn't show weapon stats
                if (__instance.isScythe()) return false;

                //DRAW DPS
                Utility.drawWithShadow(spriteBatch, Game1.mouseCursors, new Vector2(LeftMargin + 4, y + 16 + 4), new Rectangle(120, 428, 10, 10), Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                Utility.drawTextWithShadow(spriteBatch, "DPS: " + Math.Round(ModWeaponStats.DPS, 1), font, new Vector2(LeftMargin + TextLeftMargin, y + 16 + 12), Color.DarkRed * alpha);
                y += (int)font.MeasureString("TT").Y;

                //DRAW MIN AND MAX DAMAGE
                Color damageColor = Game1.textColor;
                if (__instance.hasEnchantmentOfType<RubyEnchantment>()) {
                    damageColor = EnchantmentColor;
                }
                Utility.drawTextWithShadow(spriteBatch, "Base: " + __instance.minDamage + "-" + __instance.maxDamage, font, new Vector2(LeftMargin + TextLeftMargin, y + 16 + 12), damageColor * 0.9f * alpha);
                y += (int)font.MeasureString("TT").Y;

                //DRAW AVERAGE DAMAGE
                Utility.drawTextWithShadow(spriteBatch, "Avg: " + Math.Round(ModWeaponStats.AvgDamage, 1), font, new Vector2(LeftMargin + TextLeftMargin, y + 16 + 12), Game1.textColor * 0.9f * alpha);
                y += (int)Math.Max(font.MeasureString("TT").Y, 48f);

                //DRAW CRIT CHANCE
                Color critChanceColor = Game1.textColor;
                if (__instance.hasEnchantmentOfType<AquamarineEnchantment>()) {
                    critChanceColor = EnchantmentColor;
                }
                Utility.drawWithShadow(spriteBatch, Game1.mouseCursors, new Vector2(LeftMargin + 4, y + 16 + 4), new Rectangle(40, 428, 10, 10), Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                Utility.drawTextWithShadow(spriteBatch, "Crit. Chance: " + Math.Round(ModWeaponStats.CritChance * 100) + "%", font, new Vector2(LeftMargin + TextLeftMargin, y + 16 + 12), critChanceColor * 0.9f * alpha);
                y += (int)Math.Max(font.MeasureString("TT").Y, 48f);

                //DRAW CRIT MULTIPLIER
                Color critMultColor = Game1.textColor;
                if (__instance.hasEnchantmentOfType<JadeEnchantment>()) {
                    critMultColor = EnchantmentColor;
                }
                Utility.drawWithShadow(spriteBatch, Game1.mouseCursors, new Vector2(LeftMargin, y + 16 + 4), new Rectangle(160, 428, 10, 10), Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                Utility.drawTextWithShadow(spriteBatch, "Crit. Mult: " + Math.Round(ModWeaponStats.CritMultiplier, 1) + "X", font, new Vector2(LeftMargin + TextLeftMargin, y + 16 + 12), critMultColor * 0.9f * alpha);
                y += (int)Math.Max(font.MeasureString("TT").Y, 48f);

                //DRAW SPEED
                Utility.drawWithShadow(spriteBatch, Game1.mouseCursors, new Vector2(LeftMargin + 4, y + 16 + 4), new Rectangle(130, 428, 10, 10), Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                Color c = Game1.textColor;
                if (__instance.hasEnchantmentOfType<EmeraldEnchantment>()) {
                    c = EnchantmentColor;
                }
                Utility.drawTextWithShadow(spriteBatch, "Speed: " + Math.Round((float)1000 / ModWeaponStats.Speed, 1) + "/s", font, new Vector2(LeftMargin + TextLeftMargin, y + 16 + 12), c * 0.9f * alpha);
                y += (int)Math.Max(font.MeasureString("TT").Y, 48f);

                //DRAW KNOCKBACK
                Color knockbackColor = Game1.textColor;
                if (__instance.hasEnchantmentOfType<AmethystEnchantment>()) {
                    knockbackColor = EnchantmentColor;
                }
                Utility.drawWithShadow(spriteBatch, Game1.mouseCursors, new Vector2(LeftMargin + 4, y + 16 + 4), new Rectangle(70, 428, 10, 10), Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                Utility.drawTextWithShadow(spriteBatch, "Knockback: " + ModWeaponStats.Knockback, font, new Vector2(LeftMargin + TextLeftMargin, y + 16 + 12), knockbackColor * 0.9f * alpha);
                y += (int)Math.Max(font.MeasureString("TT").Y, 48f);

                //DRAW DEFENSE
                if (__instance.addedDefense.Value > 0) {
                    Color defenseColor = Game1.textColor;
                    if (__instance.hasEnchantmentOfType<TopazEnchantment>()) {
                        defenseColor = EnchantmentColor;
                    }
                    Utility.drawWithShadow(spriteBatch, Game1.mouseCursors, new Vector2(LeftMargin + 4, y + 16 + 4), new Rectangle(110, 428, 10, 10), Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                    Utility.drawTextWithShadow(spriteBatch, "Defense: " + __instance.addedDefense.Value, font, new Vector2(LeftMargin + TextLeftMargin, y + 16 + 12), defenseColor * 0.9f * alpha);
                    y += (int)Math.Max(font.MeasureString("TT").Y, 48f);
                }

                //DRAW ENCHANTMENTS
                //unchanged from base game
                if (__instance.enchantments.Count > 0 && __instance.enchantments[__instance.enchantments.Count - 1] is DiamondEnchantment) {
                    Color c6 = EnchantmentColor;
                    int random_forges = __instance.GetMaxForges() - __instance.GetTotalForgeLevels();
                    string random_forge_string = ((random_forges == 1) ? Game1.content.LoadString("Strings\\UI:ItemHover_DiamondForge_Singular", random_forges) : Game1.content.LoadString("Strings\\UI:ItemHover_DiamondForge_Plural", random_forges));
                    Utility.drawTextWithShadow(spriteBatch, random_forge_string, font, new Vector2(x + 16, y + 16 + 12), c6 * 0.9f * alpha);
                    y += (int)Math.Max(font.MeasureString("TT").Y, 48f);
                }
                foreach (BaseEnchantment enchantment in __instance.enchantments) {
                    if (enchantment.ShouldBeDisplayed()) {
                        Color c7 = new Color(120, 0, 210);
                        if (enchantment.IsSecondaryEnchantment()) {
                            Utility.drawWithShadow(spriteBatch, Game1.mouseCursors_1_6, new Vector2(x + 16 + 4, y + 16 + 4), new Rectangle(502, 430, 10, 10), Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                            c7 = new Color(120, 50, 100);
                        } else {
                            Utility.drawWithShadow(spriteBatch, Game1.mouseCursors2, new Vector2(x + 16 + 4, y + 16 + 4), new Rectangle(127, 35, 10, 10), Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                        }
                        Utility.drawTextWithShadow(spriteBatch, ((BaseEnchantment.hideEnchantmentName && !enchantment.IsSecondaryEnchantment()) || (BaseEnchantment.hideSecondaryEnchantName && enchantment.IsSecondaryEnchantment())) ? "???" : enchantment.GetDisplayName(), font, new Vector2(x + 16 + 52, y + 16 + 12), c7 * 0.9f * alpha);
                        y += (int)Math.Max(font.MeasureString("TT").Y, 48f);
                    }
                }

                // don't run original method since we've overridden it entirely
                return false; 
            } catch (Exception ex) {
                ModMonitor.Log($"Failed in {nameof(drawTooltip_Prefix)}:\n{ex.Message}", LogLevel.Error);
                // run original method as a fallback
                return true; 
            }
        }

        public static bool getExtraSpace_Prefix(MeleeWeapon __instance, ref Point __result, SpriteFont font, int minWidth, int horizontalBuffer, string boldTitleText, int moneyAmountToDisplayAtBottom) {
            try {
                int maxStat = 9999;
                Point dimensions = new Point(0, 0);
                var ModGetDescriptionWidth = AccessTools.Method(__instance.GetType().BaseType, "getDescriptionWidth");

                //title plus money value
                dimensions.Y += Math.Max(60, (int)((boldTitleText != null) ? (Game1.dialogueFont.MeasureString(boldTitleText).Y + 16f) : 0f) + 32) + (int)font.MeasureString("T").Y + (int)((moneyAmountToDisplayAtBottom > -1) ? (font.MeasureString(moneyAmountToDisplayAtBottom.ToString() ?? "").Y + 4f) : 0f);
                //description
                dimensions.Y += (int)font.MeasureString(Game1.parseText(__instance.description, Game1.smallFont, (int)ModGetDescriptionWidth.Invoke(__instance, null)!)).Y;

                //space for our modded stats!
                //we'll always show the same stats, so the vertical can be a static value
                dimensions.Y += ((__instance.isScythe()) ? 0 : 310);
                //measure out the horizontal space like in the base game
                dimensions.X = (int)Math.Max(minWidth, 
                    Math.Max(font.MeasureString("Base: " + maxStat + "-" + maxStat).X + (float)horizontalBuffer, 
                    Math.Max(font.MeasureString("Speed: " + maxStat + "/s").X + (float)horizontalBuffer, 
                    Math.Max(font.MeasureString("Defense: " + maxStat).X + (float)horizontalBuffer, 
                    Math.Max(font.MeasureString("Crit. Chance: " + maxStat + "%").X + (float)horizontalBuffer, 
                    Math.Max(font.MeasureString("Crit. Mult: " + maxStat + "X").X + (float)horizontalBuffer, 
                    font.MeasureString("Knockback: " + maxStat).X + (float)horizontalBuffer)))))
                    );

                //add vertical space for defense since it only shows when it's >0
                if (__instance.addedDefense.Value > 0) {
                    dimensions.Y += (int)Math.Max(font.MeasureString("TT").Y, 48f);
                }

                //enchantments
                if (__instance.enchantments.Count > 0 && __instance.enchantments[__instance.enchantments.Count - 1] is DiamondEnchantment) {
                    dimensions.X = (int)Math.Max(dimensions.X, font.MeasureString(Game1.content.LoadString("Strings\\UI:ItemHover_DiamondForge_Plural", __instance.GetMaxForges())).X);
                }
                foreach (BaseEnchantment enchantment in __instance.enchantments) {
                    if (enchantment.ShouldBeDisplayed()) {
                        dimensions.Y += (int)Math.Max(font.MeasureString("TT").Y, 48f);
                    }
                }

                //this variable passes the return value to the calling method
                __result = dimensions;
                // don't run original method since we've overridden it entirely
                return false; 
            } catch (Exception ex) {
                ModMonitor.Log($"Failed in {nameof(getExtraSpace_Prefix)}:\n{ex.Message}", LogLevel.Error);
                // run original method as a fallback
                return true;
            }
        }

        public static WeaponStats GetWeaponStats(MeleeWeapon weapon) {
            //get the basic min/max damage (from StardewValley.Tools.MeleeWeapon.DoDamage)
            //mostly just using floats for damage since a lot of the values are averages
            int minDamage = (int)((float)weapon.minDamage.Value * (1f + Game1.player.buffs.AttackMultiplier));
            int maxDamage = (int)((float)weapon.maxDamage.Value * (1f + Game1.player.buffs.AttackMultiplier));

            //calculate an average damage to account for randomization between min and max
            float avgDamage = (((float)maxDamage - (float)minDamage) / 2f) + (float)minDamage;

            //get the crit chance
            float critChance = weapon.critChance.Value;
            if (weapon.type.Value == 1) {
                //added crit chance for daggers for some reason (from DoDamage)
                critChance = (critChance + 0.005f) * 1.12f;
            }
            critChance *= 1f + Game1.player.buffs.CriticalChanceMultiplier;

            //statue buff to crit chance (from StardewValley.GameLocation.damageMonster)
            if (Game1.player.hasBuff("statue_of_blessings_5")) {
                critChance += 0.1f;
            }
            //Scout proffession buff to crit chance (from damageMonster)
            if (Game1.player.professions.Contains(25)) {
                critChance += critChance * 0.5f;
            }

            //clamp crit chance to 0-100%
            critChance = Math.Max(0, Math.Min(1, critChance));

            //get the crit multiplier and apply any buffs
            float critMult = weapon.critMultiplier.Value * (1f + Game1.player.buffs.CriticalPowerMultiplier);

            //calculate critical damage based on the chance to happen
            float avgCritDamage = critChance * critMult * avgDamage;

            //now add the average crit damage to the average damage
            float avgTotalDamage = avgDamage + avgCritDamage;

            //add any attack buffs
            avgTotalDamage += Game1.player.Attack * 3;

            //Fighter proffession buff to damage (from damageMonster)
            if (Game1.player.professions.Contains(24)) {
                avgTotalDamage = (float)Math.Ceiling(avgTotalDamage * 1.1f);
            }
            //Brute proffession buff to damage (from damageMonster)
            if (Game1.player.professions.Contains(26)) {
                avgTotalDamage = (float)Math.Ceiling(avgTotalDamage * 1.15f);
            }
            //Desperado proffession buff to crit damage (from damageMonster)
            if (Game1.player.professions.Contains(29)) {
                avgTotalDamage = avgTotalDamage + (avgTotalDamage * 2f * critChance);
            }

            //get weapon speed from StardewValley.Tools.MeleeWeapon.setFarmerAnimating
            float speed = (float)(400 - weapon.speed.Value * 40) - Game1.player.addedSpeed * 40f;
            speed *= 1f - Game1.player.buffs.WeaponSpeedMultiplier;

            //some per-type speed changes from setFarmerAnimating
            // and StardewValley.Tools.MeleeWeapon.doSwipe
            if (weapon.type.Value != 1) {
                //if not a dagger
                if (weapon.type.Value == 2) {
                    //clubs
                    speed /= 5f;
                } else {
                    //swords
                    speed /= 8f;
                }
                speed *= 1.3f;
            } else {
                //if dagger
                speed /= 4f;
            }

            //from StardewValley.FarmerSprite.getAnimationFromIndex
            if (weapon.type.Value != 1) {
                //175 is the sum of the attack animation frame lengths (in ms) that occur before the final frame where the speed value is actually used
                speed = 175 + (speed * 2f);
            } else {
                //daggers are different. they just animate in two frames at speed
                speed *= 2f;
            }

            //calculate DPS, converting speed into attacks per second
            float DPS = avgTotalDamage * (1000 / speed);

            //get knockback from weapon stats and apply buffs
            float knockback = weapon.knockback.Value * (1f + Game1.player.buffs.KnockbackMultiplier);

            return new WeaponStats { AvgDamage = avgTotalDamage, DPS = DPS, Speed = (int)speed, CritChance = critChance, CritMultiplier = critMult, Knockback = knockback };
        }
    }
}
