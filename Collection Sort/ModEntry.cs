using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace BetterCollectionSorting {
    internal struct ObjectListStruct {
        public string ID; //has to be a string as some IDs contain letters
        public string Name;
        public int Price;
        public int Count;
        public string Color;
        public ClickableTextureComponent CollectionItem;
    }
    internal enum ObjectSorts {
        Logical,
        Alpha,
        Price,
        Count, //could be # shipped, caught, found, or cooked
        Color
    }


    // Class to hold all tab-specific data
    internal class CollectionTabData {
        // Simple name for internal use
        public string SimpleName { get; set; }

        // Translated name for tooltips
        public string TranslatedName { get; set; }

        // Current sort mode for this tab
        public ObjectSorts CurrentSort { get; set; }

        // Filename for the logical sort order JSON
        public string SortOrderFilename { get; set; }

        // Array of item IDs in logical sort order
        public string[] LogicalSortOrder { get; set; }

        // Map of sort enum values to display names for tooltips
        public Dictionary<int, string> SortDisplayNames { get; set; }

        // List of objects in this collection tab
        public List<ObjectListStruct> ObjectList { get; set; }

        public CollectionTabData(string simpleName, string translatedName, string sortOrderFilename, string countSortLabel) {
            SimpleName = simpleName;
            TranslatedName = translatedName;
            SortOrderFilename = sortOrderFilename;
            CurrentSort = ObjectSorts.Logical;
            LogicalSortOrder = Array.Empty<string>();
            ObjectList = new List<ObjectListStruct>();

            SortDisplayNames = new Dictionary<int, string> {
                [(int)ObjectSorts.Logical] = ModEntry.LogicalSortLabel,
                [(int)ObjectSorts.Alpha] = ModEntry.AlphaSortLabel,
                [(int)ObjectSorts.Price] = ModEntry.PriceSortLabel,
                [(int)ObjectSorts.Count] = countSortLabel,
                [(int)ObjectSorts.Color] = ModEntry.ColorSortLabel
            };
        }
    }

    internal sealed class ModEntry : Mod {
        //need a static reference for the SMAPI monitor so that the static methods for harmony patches can access it
        private static IMonitor ModMonitor = null!;

        private static int PreviousTab = CollectionsPage.organicsTab;

        // Main dictionary to hold all tab data
        private static Dictionary<int, CollectionTabData> CollectionTabs = new Dictionary<int, CollectionTabData>();

        //holds color sorting data
        private static Dictionary<string, int> ColorSortOrder = new Dictionary<string, int>();

        //caching the tooltip language strings for performance
        internal static string LogicalSortLabel = "Logical Sort";
        internal static string AlphaSortLabel = "Alphabetical Sort";
        internal static string PriceSortLabel = "Price Sort";
        internal static string ColorSortLabel = "Color Sort";
        internal static string ShippedSortLabel = "Shipped Sort";
        internal static string CaughtSortLabel = "Caught Sort";
        internal static string FoundSortLabel = "Found Sort";
        internal static string CookedSortLabel = "Cooked Sort";

        public override void Entry(IModHelper helper) {
            var harmony = new Harmony(this.ModManifest.UniqueID);
            ModMonitor = this.Monitor;

            //this event should be triggered on locale change or game load unless you're starting the game in English. so we'll just call this once at game start as well to make sure the strings are loaded
            helper.Events.Content.LocaleChanged += this.LocaleChanged;
            helper.Events.GameLoop.GameLaunched += this.GameLaunched;

            //init our main data store
            CollectionTabs[CollectionsPage.organicsTab] = new CollectionTabData(
                "items",
                Game1.content.LoadString("Strings\\UI:Collections_Shipped"),
                "item_sorting.json",
                ShippedSortLabel
            );
            CollectionTabs[CollectionsPage.fishTab] = new CollectionTabData(
                "fish",
                Game1.content.LoadString("Strings\\UI:Collections_Fish"),
                "fish_sorting.json",
                CaughtSortLabel
            );
            CollectionTabs[CollectionsPage.archaeologyTab] = new CollectionTabData(
                "artifacts",
                Game1.content.LoadString("Strings\\UI:Collections_Artifacts"),
                "artifact_sorting.json",
                FoundSortLabel
            );
            CollectionTabs[CollectionsPage.mineralsTab] = new CollectionTabData(
                "minerals",
                Game1.content.LoadString("Strings\\UI:Collections_Minerals"),
                "mineral_sorting.json",
                FoundSortLabel
            );
            CollectionTabs[CollectionsPage.cookingTab] = new CollectionTabData(
                "cooking",
                Game1.content.LoadString("Strings\\UI:Collections_Cooking"),
                "cooking_sorting.json",
                CookedSortLabel
            );

            // Load sort orders for each tab
            foreach (var tabData in CollectionTabs) {
                LoadSortOrder(tabData.Value);
            }

            // load color sorting from JSON
            try {
                string[] colorOrder = this.Helper.Data.ReadJsonFile<string[]>(Path.Combine("assets", "color_sorting.json"))!;

                if (colorOrder == null) {
                    throw new Exception("Failed to load data from color_sorting.json or file was empty or missing.");
                } else if (colorOrder.Length == 0) {
                    throw new Exception("File color_sorting.json was empty.");
                } else {
                    // Create a dictionary mapping colors to their order index
                    int colorIndex;
                    for (colorIndex = 0; colorIndex < colorOrder.Length; colorIndex++) {
                        ColorSortOrder[colorOrder[colorIndex]] = colorIndex;
                    }
                    
                    Monitor.Log($"Loaded {colorOrder.Length} sort IDs for color", LogLevel.Debug);
                }
            } catch (Exception ex) {
                Monitor.Log($"Error loading sort order for colors: {ex.Message}", LogLevel.Warn);
            }
            // add a default color to the order in case items don't have a color or color order loading failed
            ColorSortOrder["no_color"] = ColorSortOrder.Count;

            // postfix patch for the collection page constructor. sets up the item data and does initial sorting.
            harmony.Patch(
               original: AccessTools.Constructor(typeof(StardewValley.Menus.CollectionsPage), new Type[] { typeof(int), typeof(int), typeof(int), typeof(int) }),
               postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.CollectionsPage_Postfix))
            );

            //pre- and postfix for left-clicking on the collection tabs. switches sort order and triggers sort, but only if the tab is already selected.
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.Menus.CollectionsPage), "receiveLeftClick", new Type[] { typeof(int), typeof(int), typeof(bool) }),
               prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.receiveLeftClick_Prefix))
            );
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.Menus.CollectionsPage), "receiveLeftClick", new Type[] { typeof(int), typeof(int), typeof(bool) }),
               postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.receiveLeftClick_Postfix))
            );

            //postfix for right-clicking on the collection tabs. switches sort order and triggers sort but to the previous sort order.
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.Menus.CollectionsPage), "receiveRightClick", new Type[] { typeof(int), typeof(int), typeof(bool) }),
               postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.receiveRightClick_Postfix))
            );
        }

        private void UpdateLocale() {
            LogicalSortLabel = this.Helper.Translation.Get("sort-logical").Default(LogicalSortLabel);
            AlphaSortLabel = this.Helper.Translation.Get("sort-alpha").Default(AlphaSortLabel);
            PriceSortLabel = this.Helper.Translation.Get("sort-price").Default(PriceSortLabel);
            ColorSortLabel = this.Helper.Translation.Get("sort-color").Default(ColorSortLabel);
            ShippedSortLabel = this.Helper.Translation.Get("sort-shipped").Default(ShippedSortLabel);
            CaughtSortLabel = this.Helper.Translation.Get("sort-caught").Default(CaughtSortLabel);
            FoundSortLabel = this.Helper.Translation.Get("sort-found").Default(FoundSortLabel);
            CookedSortLabel = this.Helper.Translation.Get("sort-cooked").Default(CookedSortLabel);

            //have to redo tab sort labels whenever translations change
            string tabLabel = null!;
            string countSortLabel = ShippedSortLabel;
            foreach (var CollectionTab in CollectionTabs) {
                switch (CollectionTab.Key) {
                    case CollectionsPage.organicsTab:
                        tabLabel = Game1.content.LoadString("Strings\\UI:Collections_Shipped");
                        countSortLabel = ShippedSortLabel;
                        break;
                    case CollectionsPage.fishTab:
                        tabLabel = Game1.content.LoadString("Strings\\UI:Collections_Fish");
                        countSortLabel = CaughtSortLabel;
                        break;
                    case CollectionsPage.archaeologyTab:
                        tabLabel = Game1.content.LoadString("Strings\\UI:Collections_Artifacts");
                        countSortLabel = FoundSortLabel;
                        break;
                    case CollectionsPage.mineralsTab:
                        tabLabel = Game1.content.LoadString("Strings\\UI:Collections_Minerals");
                        countSortLabel = FoundSortLabel;
                        break;
                    case CollectionsPage.cookingTab:
                        tabLabel = Game1.content.LoadString("Strings\\UI:Collections_Cooking");
                        countSortLabel = CookedSortLabel;
                        break;
                }

                CollectionTab.Value.SortDisplayNames = new Dictionary<int, string> {
                    [(int)ObjectSorts.Logical] = ModEntry.LogicalSortLabel,
                    [(int)ObjectSorts.Alpha] = ModEntry.AlphaSortLabel,
                    [(int)ObjectSorts.Price] = ModEntry.PriceSortLabel,
                    [(int)ObjectSorts.Count] = countSortLabel,
                    [(int)ObjectSorts.Color] = ModEntry.ColorSortLabel
                };
                if (tabLabel != null) {
                    CollectionTab.Value.TranslatedName = tabLabel;
                }
            }
        }

        private void LocaleChanged(object? sender, LocaleChangedEventArgs e) {
            UpdateLocale();
        }

        private void GameLaunched(object? sender, GameLaunchedEventArgs e) {
            UpdateLocale();
        }

        private void LoadSortOrder(CollectionTabData tabData) {
            try {
                // Use SMAPI's helper to read the JSON file
                // it's less strict about JSON format and allows for comments
                string[] sortOrder = this.Helper.Data.ReadJsonFile<string[]>(Path.Combine("assets", tabData.SortOrderFilename))!;

                if (sortOrder == null) {
                    throw new Exception($"Failed to load data from {tabData.SortOrderFilename} or file was empty or missing.");
                } else if (sortOrder.Length == 0) {
                    throw new Exception($"File {tabData.SortOrderFilename} was empty.");
                }

                tabData.LogicalSortOrder = sortOrder;

                // Log the loaded data
                Monitor.Log($"Loaded {sortOrder.Length} sort IDs for {tabData.SimpleName}", LogLevel.Debug);
            } catch (Exception ex) {
                Monitor.Log($"Error loading sort order for {tabData.SimpleName}: {ex.Message}", LogLevel.Warn);
            }
        }

        public static void CollectionsPage_Postfix(StardewValley.Menus.CollectionsPage __instance, int x, int y, int width, int height) {
            //for each collection tab
            foreach (KeyValuePair<int, List<List<ClickableTextureComponent>>> CollectionTab in __instance.collections) {
                // Only process tabs we've configured
                if (CollectionTabs.ContainsKey(CollectionTab.Key)) {
                    var tabData = CollectionTabs[CollectionTab.Key];

                    // Clear and initialize object list
                    tabData.ObjectList = new List<ObjectListStruct>();

                    // for each tab page
                    foreach (List<ClickableTextureComponent> CollectionPage in CollectionTab.Value) {
                        // for each item in page
                        foreach (ClickableTextureComponent CollectionItem in CollectionPage) {
                            string ItemID = ArgUtility.SplitBySpace(CollectionItem.name).First();
                            ParsedItemData ItemData = ItemRegistry.GetDataOrErrorItem(ItemID);

                            // getting the counts is different for each item type
                            int ItemCount = 0;
                            switch (CollectionTab.Key) {
                                case CollectionsPage.organicsTab:
                                    ItemCount = Game1.player.basicShipped.TryGetValue(ItemID, out var timesCount) ? timesCount : 0;
                                    break;
                                case CollectionsPage.fishTab:
                                    ItemCount = Game1.player.fishCaught.TryGetValue("(O)" + ItemID, out var timesCaught) ? timesCaught[0] : 0;
                                    break;
                                case CollectionsPage.archaeologyTab:
                                    ItemCount = Game1.player.archaeologyFound.TryGetValue(ItemID, out var timesFound1) ? timesFound1[0] : 0;
                                    break;
                                case CollectionsPage.mineralsTab:
                                    ItemCount = Game1.player.mineralsFound.TryGetValue(ItemID, out var timesFound2) ? timesFound2 : 0;
                                    break;
                                case CollectionsPage.cookingTab:
                                    ItemCount = Game1.player.recipesCooked.TryGetValue(ItemID, out var timesCooked) ? timesCooked : 0;
                                    break;
                            }

                            tabData.ObjectList.Add(new ObjectListStruct {
                                ID = ItemID,
                                Name = Game1.content.LoadStringReturnNullIfNotFound("Strings\\Objects:" + ItemID + "_CollectionsTabName") ?? ItemData.DisplayName,  //translated string or the display name if not found
                                Price = ObjectDataDefinition.GetRawPrice(ItemData), //will return 0 if not found
                                Count = ItemCount,
                                Color = GetItemColor(ItemID), //will return "no_color" if not found
                                CollectionItem = CollectionItem
                            });
                        }
                    }

                    //do the list sort
                    List<ObjectListStruct> ItemListSorted = SortItems(CollectionTab.Key);

                    //update the internal graphical list
                    ApplySort(__instance, ItemListSorted, CollectionTab.Key);
                }
            }
        }

        // gets the item's color from the "context tags"
        public static string GetItemColor(string ItemID) {
            HashSet<string> ItemContextTags = ItemContextTagManager.GetBaseContextTags(ItemID);
            foreach (KeyValuePair<string,int> ColorTag in ColorSortOrder) {
                if (ItemContextTagManager.DoesTagMatch(ColorTag.Key, ItemContextTags)) {
                    return ColorTag.Key;
                }
            }
            //default value that will be sorted to the end
            return "no_color";
        }

        public static bool receiveLeftClick_Prefix(StardewValley.Menus.CollectionsPage __instance, int x, int y, bool playSound = true) {
            //just saving the previous selected tab for postfix
            PreviousTab = __instance.currentTab;
            return true;
        }

        public static void receiveLeftClick_Postfix(StardewValley.Menus.CollectionsPage __instance, int x, int y, bool playSound = true) {
            foreach (KeyValuePair<int, ClickableTextureComponent> SideTab in __instance.sideTabs) {
                //if the user clicked on a tab and the tab is the same as before the user clicked and it's one of our modded tabs, then change to the next sort
                if (SideTab.Value.containsPoint(x, y) && __instance.currentTab == PreviousTab && CollectionTabs.ContainsKey(__instance.currentTab)) {
                    //play a little sound for user feedback
                    Game1.playSound("smallSelect", null);
                    //set the page to 0 on the current tab so we start at the front of the new sorting
                    __instance.currentPage = 0;

                    var tabData = CollectionTabs[__instance.currentTab];

                    //increment the sort index and loop around to the beginning
                    tabData.CurrentSort = (ObjectSorts)(((int)tabData.CurrentSort + 1) % Enum.GetValues(typeof(ObjectSorts)).Length);

                    //do the list sort
                    List<ObjectListStruct> ItemCollectionSorted = SortItems(__instance.currentTab);

                    //update the internal graphical list
                    ApplySort(__instance, ItemCollectionSorted, __instance.currentTab);
                    break;
                }
            }
        }

        public static void receiveRightClick_Postfix(StardewValley.Menus.CollectionsPage __instance, int x, int y, bool playSound = true) {
            foreach (KeyValuePair<int, ClickableTextureComponent> SideTab in __instance.sideTabs) {
                //if current tab selected and it's one of our modded ones
                if (SideTab.Value.containsPoint(x, y) && __instance.currentTab == SideTab.Key && CollectionTabs.ContainsKey(__instance.currentTab)) {
                    Game1.playSound("smallSelect", null);
                    __instance.currentPage = 0;
                    var tabData = CollectionTabs[__instance.currentTab];

                    //decrement the sort index and loop around to the end
                    int enumLength = Enum.GetValues(typeof(ObjectSorts)).Length;
                    tabData.CurrentSort = (ObjectSorts)(((int)tabData.CurrentSort + enumLength - 1) % enumLength);

                    List<ObjectListStruct> ItemCollectionSorted = SortItems(__instance.currentTab);
                    ApplySort(__instance, ItemCollectionSorted, __instance.currentTab);
                    break;
                }
            }
        }

        private static List<ObjectListStruct> SortItems(int SelectedTab) {
            if (!CollectionTabs.ContainsKey(SelectedTab)) {
                ModMonitor.Log($"Tried to sort unknown tab {SelectedTab}", LogLevel.Error);
                return new List<ObjectListStruct>();
            }

            var CurTab = CollectionTabs[SelectedTab];
            List<ObjectListStruct> ObjectListSorted;

            switch (CurTab.CurrentSort) {
                case ObjectSorts.Alpha:
                    ObjectListSorted = new List<ObjectListStruct>(CurTab.ObjectList); //copy by value
                    //ascending
                    ObjectListSorted.Sort((a, b) => a.Name.CompareTo(b.Name));
                    break;
                case ObjectSorts.Price:
                    ObjectListSorted = new List<ObjectListStruct>(CurTab.ObjectList); //copy by value
                    ObjectListSorted.Sort((a, b) => {
                        //first sort by price descending
                        int priceCompare = b.Price.CompareTo(a.Price);
                        if (priceCompare == 0) {
                            //then sort by name ascending
                            return a.Name.CompareTo(b.Name);
                        }
                        return priceCompare;
                    });
                    break;
                case ObjectSorts.Count:
                    ObjectListSorted = new List<ObjectListStruct>(CurTab.ObjectList); //copy by value
                    ObjectListSorted.Sort((a, b) => {
                        //first sort by shipping count descending
                        int countCompare = b.Count.CompareTo(a.Count);
                        if (countCompare == 0) {
                            //then sort by name ascending
                            return a.Name.CompareTo(b.Name);
                        }
                        return countCompare;
                    });
                    break;
                case ObjectSorts.Color:
                    ObjectListSorted = new List<ObjectListStruct>(CurTab.ObjectList); //copy by value
                    ObjectListSorted.Sort((a, b) => {
                        // sort by our custom color sort first
                        int colorCompare = ColorSortOrder[a.Color].CompareTo(ColorSortOrder[b.Color]);
                        if (colorCompare == 0) {
                            //then sort by name ascending
                            return a.Name.CompareTo(b.Name);
                        }
                        return colorCompare;
                    });
                    break;
                default:
                case ObjectSorts.Logical:
                    // sort the items using our custom logical order
                    ObjectListSorted = new List<ObjectListStruct>();

                    //create a copy so we don't modify our original
                    List<ObjectListStruct> ItemCollectionCopy = new List<ObjectListStruct>(CurTab.ObjectList); //copy by value

                    foreach (string SortItemID in CurTab.LogicalSortOrder) {
                        for (int i = 0; i < ItemCollectionCopy.Count; i++) {
                            if (ItemCollectionCopy[i].ID == SortItemID) {
                                ObjectListSorted.Add(ItemCollectionCopy[i]);
                                ItemCollectionCopy.RemoveAt(i);
                                break;
                            }
                        }
                    }
                    //add any leftovers at the end with a message so users know, in case they messed up the sort order list
                    if (ItemCollectionCopy.Count > 0) {
                        ModMonitor.Log($"Appended {ItemCollectionCopy.Count} unsorted items in {CurTab.SimpleName} tab.", LogLevel.Debug);
                        ObjectListSorted.AddRange(ItemCollectionCopy);
                    }
                    break;
            }

            //ModMonitor.Log($"Tab {SelectedTab} ({CurTab.SimpleName}) sorted with order: {CurTab.CurrentSort}.", LogLevel.Debug);

            return ObjectListSorted;
        }

        private static void ApplySort(StardewValley.Menus.CollectionsPage __instance, List<ObjectListStruct> CurrentObjectListSorted, int SelectedTab) {
            if (!CollectionTabs.ContainsKey(SelectedTab)) {
                return;
            }

            int ItemsOnRow = 0;
            int MaxItemsPerRow = 10;

            //this logic is all reproduced from the game code.
            //it positions the item list relative to the menu position
            int xPositionOnScreen = (int)AccessTools.Field(__instance.GetType().BaseType, "xPositionOnScreen").GetValue(__instance)!;
            int yPositionOnScreen = (int)AccessTools.Field(__instance.GetType().BaseType, "yPositionOnScreen").GetValue(__instance)!;
            int baseX = xPositionOnScreen + IClickableMenu.borderWidth + IClickableMenu.spaceToClearSideBorder;
            int baseY = yPositionOnScreen + IClickableMenu.borderWidth + IClickableMenu.spaceToClearTopBorder - 16;

            __instance.collections[SelectedTab].Clear();

            foreach (ObjectListStruct ItemSorted in CurrentObjectListSorted) {
                int xPos = baseX + ItemsOnRow % MaxItemsPerRow * 68;
                int yPos = baseY + ItemsOnRow / MaxItemsPerRow * 68;

                //add new page
                if (yPos > yPositionOnScreen + __instance.height - 128) {
                    __instance.collections[SelectedTab].Add(new List<ClickableTextureComponent>());
                    ItemsOnRow = 0;
                    xPos = baseX;
                    yPos = baseY;
                }
                //add first page
                if (__instance.collections[SelectedTab].Count == 0) {
                    __instance.collections[SelectedTab].Add(new List<ClickableTextureComponent>());
                }

                //Monitor.Log($"Item ID: {ItemSorted.ID}, Name: {ItemSorted.Name}, Price: {ItemSorted.Price}, Count: {ItemSorted.Count}, Count: {ItemSorted.Color}", LogLevel.Debug);

                //get last page
                List<ClickableTextureComponent> CurrentPageList = __instance.collections[SelectedTab].Last();

                //basically just creating a new reference to ItemSorted.CollectionItem for readability
                //it's a new copy since the foreach copied ItemStat by value
                ClickableTextureComponent CollectionItem = ItemSorted.CollectionItem;

                CollectionItem.myID = CurrentPageList.Count;

                //set the new coordinates
                CollectionItem.bounds = new Rectangle(xPos, yPos, 64, 64);

                // Determine horizontal neighbors
                // (not really sure what this does but it's generated all throughout stardew's code)
                bool isRightEdge = (CurrentPageList.Count + 1) % MaxItemsPerRow == 0;
                bool isLeftEdge = CurrentPageList.Count % MaxItemsPerRow == 0;
                CollectionItem.rightNeighborID = isRightEdge ? -1 : (CurrentPageList.Count + 1);
                CollectionItem.leftNeighborID = isLeftEdge ? 7001 : (CurrentPageList.Count - 1);

                // Determine vertical neighbors
                bool isBottomEdge = yPos + 68 > yPositionOnScreen + __instance.height - 128;
                bool isTopRow = CurrentPageList.Count < MaxItemsPerRow;
                CollectionItem.downNeighborID = isBottomEdge ? -7777 : (CurrentPageList.Count + MaxItemsPerRow);
                CollectionItem.upNeighborID = isTopRow ? 12347 : (CurrentPageList.Count - MaxItemsPerRow);

                CurrentPageList.Add(CollectionItem);
                ItemsOnRow++;
            }

            // Update the tooltip to show current sort mode
            __instance.sideTabs[SelectedTab].hoverText = 
                CollectionTabs[SelectedTab].TranslatedName + "\n" + 
                CollectionTabs[SelectedTab].SortDisplayNames[(int)CollectionTabs[SelectedTab].CurrentSort];
        }
    }
}