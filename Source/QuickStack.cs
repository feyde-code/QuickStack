using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;

public enum QuickStackType : byte
{
    Stack = 0,
    Restock,
    Count
}

internal class QuickStack
{
    public static string configFilePath;
    public static float[] lastClickTimes = new float[(int)QuickStackType.Count];
    public static bool lockModeIconVisible = true;
    public static Vector3i stashDistance = new Vector3i(7, 7, 7);
    public static XUiC_Backpack playerBackpack;
    public static XUiC_BackpackWindow backpackWindow;
    public static XUiC_ContainerStandardControls playerControls;
    public static KeyCode[] quickLockHotkeys;
    public static KeyCode[] quickStackHotkeys;
    public static KeyCode[] quickRestockHotkeys;
    public static Color32 lockIconColor = new Color32(255, 0, 0, 255);
    public static Color32 lockBorderColor = new Color32(128, 0, 0, 0);

    public static void printExceptionInfo(Exception e)
    {
        Log.Warning($"[QuickStack] {e.Message}");
        Log.Warning($"[QuickStack] {e.StackTrace}");
    }

    // Checks if a loot container is openable by a player
    // HOST OR SERVER ONLY
    public static bool IsContainerUnlocked(int _entityIdThatOpenedIt, TileEntity _tileEntity)
    {
        try
        {
            if (!ConnectionManager.Instance.IsServer)
            {
                return false;
            }

            if (_tileEntity == null)
            {
                return false;
            }

            // Handle locked containers
            if ((_tileEntity is TileEntitySecureLootContainer lootContainer) && lootContainer.IsLocked())
            {
                // Handle Host
                if (!GameManager.IsDedicatedServer && _entityIdThatOpenedIt == GameManager.Instance.World.GetPrimaryPlayerId())
                {
                    if (!lootContainer.IsUserAllowed(GameManager.Instance.persistentLocalPlayer.PrimaryId))
                    {
                        return false;
                    }
                }
                else
                {
                    // Handle Client
                    var cinfo = ConnectionManager.Instance.Clients.ForEntityId(_entityIdThatOpenedIt);
                    if (cinfo == null || !lootContainer.IsUserAllowed(cinfo.CrossplatformId))
                    {
                        return false;
                    }
                }
            }
            // Handle locked composite storages
            else if ((_tileEntity is TileEntityComposite compositeContainer) && compositeContainer.teData.GetFeatureIndex<TEFeatureLockable>() > 0)
            {
                TEFeatureLockable fLockable = compositeContainer.GetFeature<TEFeatureLockable>();
                TEFeatureStorage fStorage = compositeContainer.GetFeature<TEFeatureStorage>();

                // Handle Host
                if (!GameManager.IsDedicatedServer && _entityIdThatOpenedIt == GameManager.Instance.World.GetPrimaryPlayerId())
                {
                    if (!fLockable.IsUserAllowed(GameManager.Instance.persistentLocalPlayer.PrimaryId))
                    {
                        return false;
                    }
                }
                else
                {
                    // Handle Client
                    var cinfo = ConnectionManager.Instance.Clients.ForEntityId(_entityIdThatOpenedIt);
                    if (cinfo == null || !fLockable.IsUserAllowed(cinfo.CrossplatformId))
                    {
                        return false;
                    }
                }
            }

            // Handle in-use containers
            if (GameManager.Instance.lockedTileEntities.ContainsKey(_tileEntity) &&
               (GameManager.Instance.World.GetEntity(GameManager.Instance.lockedTileEntities[_tileEntity]) is EntityAlive entityAlive) &&
                !entityAlive.IsDead())
            {
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
            return false;
        }
    }

    public static bool IsValidLoot(TileEntityLootContainer _tileEntity)
    {
        try
        {
            return (_tileEntity.GetTileEntityType() == TileEntityType.Loot ||
                _tileEntity.GetTileEntityType() == TileEntityType.SecureLoot ||
                _tileEntity.GetTileEntityType() == TileEntityType.SecureLootSigned ||
                _tileEntity.lootListName == "cupboard") && _tileEntity.lootListName != "playerGunSafe" && _tileEntity.GetType().ToString() != "TileEntityClaimAutoRepair"; ;
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
            return false;
        }
    }

    public static (ITileEntityLootable, TileEntity) GetInventoryFromBlockPosition(Vector3i position)
    {
        try
        {
            TileEntityLootContainer lootContainer = GameManager.Instance.World.GetTileEntity(0, position) as TileEntityLootContainer;

            if (lootContainer != null)
            {
                if (IsValidLoot(lootContainer) && !lootContainer.IsUserAccessing())
                    return (lootContainer, lootContainer);

                return (null, lootContainer);
            }

            TileEntityComposite compositeContainer = GameManager.Instance.World.GetTileEntity(0, position) as TileEntityComposite;

            if (compositeContainer == null)
                return (null, null);

            if (compositeContainer.teData.GetFeatureIndex<TEFeatureStorage>() == 0)
                return (null, compositeContainer);

            if (compositeContainer.IsUserAccessing())
                return (null, compositeContainer);

            return (compositeContainer.GetFeature<TEFeatureStorage>(), compositeContainer);
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
            return (null, null);
        }
    }

    // Yields all openable loot containers in a cubic radius about a point
    public static IEnumerable<ValueTuple<Vector3i, TileEntity>> FindNearbyLootContainers(Vector3i _center, int _playerEntityId)
    {
        for (int i = -stashDistance.x; i <= stashDistance.x; i++)
        {
            for (int j = -stashDistance.y; j <= stashDistance.y; j++)
            {
                for (int k = -stashDistance.z; k <= stashDistance.z; k++)
                {
                    var offset = new Vector3i(i, j, k);

                    var val = GetInventoryFromBlockPosition(_center + offset);

                    if (val.Item1 == null)
                        continue;

                    if (!IsContainerUnlocked(_playerEntityId, val.Item2))
                        continue;

                    yield return new ValueTuple<Vector3i, TileEntity>(offset, val.Item2);
                }
            }
        }
    }

    // Gets the EItemMoveKind for the current move type based on the last time that move type was requested
    internal static XUiM_LootContainer.EItemMoveKind GetMoveKind(QuickStackType _type = QuickStackType.Stack)
    {
        try
        {
            float unscaledTime = Time.unscaledTime;
            float lastClickTime = lastClickTimes[(int)_type];
            lastClickTimes[(int)_type] = unscaledTime;

            if (unscaledTime - lastClickTime < 2.0f)
            {
                return XUiM_LootContainer.EItemMoveKind.FillAndCreate;
            }
            else
            {
                return XUiM_LootContainer.EItemMoveKind.FillOnly;
            }
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
            return XUiM_LootContainer.EItemMoveKind.FillOnly;
        }
    }

    // Quickstack functionality
    // SINGLEPLAYER ONLY
    public static void MoveQuickStack()
    {
        try
        {
            if (backpackWindow.xui.lootContainer != null && backpackWindow.xui.lootContainer.EntityId == -1)
                return;

            var moveKind = GetMoveKind();

            EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();

            for (int i = -stashDistance.x; i <= stashDistance.x; i++)
            {
                for (int j = -stashDistance.y; j <= stashDistance.y; j++)
                {
                    for (int k = -stashDistance.z; k <= stashDistance.z; k++)
                    {
                        Vector3i blockPos = new Vector3i((int)primaryPlayer.position.x + i, (int)primaryPlayer.position.y + j, (int)primaryPlayer.position.z + k);

                        var val = GetInventoryFromBlockPosition(blockPos);

                        if (val.Item1 == null)
                            continue;

                        XUiM_LootContainer.StashItems(backpackWindow, playerBackpack, val.Item1, 0, playerControls.LockedSlots, moveKind, playerControls.MoveStartBottomRight);
                        val.Item2.SetModified();
                    }
                }
            }
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
        }
    }

    public static void ClientMoveQuickStack(Vector3i center, IEnumerable<Vector3i> _entityContainers)
    {
        try
        {
            if (backpackWindow.xui.lootContainer != null && backpackWindow.xui.lootContainer.EntityId == -1)
                return;

            var moveKind = GetMoveKind();

            if (_entityContainers == null)
                return;

            foreach (var offset in _entityContainers)
            {
                var val = GetInventoryFromBlockPosition(center + offset);

                if (val.Item1 == null)
                    continue;

                XUiM_LootContainer.StashItems(backpackWindow, playerBackpack, val.Item1, 0, playerControls.LockedSlots, moveKind, playerControls.MoveStartBottomRight);
                val.Item2.SetModified();
            }
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
        }
    }

    // Restock functionality
    // SINGLEPLAYER ONLY
    public static void MoveQuickRestock()
    {
        try
        {
            if (backpackWindow.xui.lootContainer != null && backpackWindow.xui.lootContainer.EntityId == -1)
                return;

            var moveKind = GetMoveKind(QuickStackType.Restock);

            EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();
            LocalPlayerUI playerUI = LocalPlayerUI.GetUIForPlayer(primaryPlayer);
            XUiC_LootWindowGroup lootWindowGroup = (XUiC_LootWindowGroup)((XUiWindowGroup)playerUI.windowManager.GetWindow("looting")).Controller;

            for (int i = -stashDistance.x; i <= stashDistance.x; i++)
            {
                for (int j = -stashDistance.y; j <= stashDistance.y; j++)
                {
                    for (int k = -stashDistance.z; k <= stashDistance.z; k++)
                    {
                        Vector3i blockPos = new Vector3i((int)primaryPlayer.position.x + i, (int)primaryPlayer.position.y + j, (int)primaryPlayer.position.z + k);

                        var val = GetInventoryFromBlockPosition(blockPos);

                        if (val.Item1 == null)
                            continue;

                        lootWindowGroup.SetTileEntityChest("QUICKSTACK", val.Item1);
                        PackedBoolArray lockedSlots = new PackedBoolArray(lootWindowGroup.lootWindow.lootContainer.items.Length);
                        XUiM_LootContainer.StashItems(backpackWindow, lootWindowGroup.lootWindow.lootContainer, playerUI.mXUi.PlayerInventory, 0, lockedSlots, moveKind, playerControls.MoveStartBottomRight);
                        val.Item2.SetModified();
                    }
                }
            }
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
        }
    }

    public static void ClientMoveQuickRestock(Vector3i center, IEnumerable<Vector3i> _entityContainers)
    {
        try
        {
            if (backpackWindow.xui.lootContainer != null && backpackWindow.xui.lootContainer.EntityId == -1)
                return;

            var moveKind = GetMoveKind(QuickStackType.Restock);

            if (_entityContainers == null)
                return;

            EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();
            LocalPlayerUI playerUI = LocalPlayerUI.GetUIForPlayer(primaryPlayer);
            XUiC_LootWindowGroup lootWindowGroup = (XUiC_LootWindowGroup)((XUiWindowGroup)playerUI.windowManager.GetWindow("looting")).Controller;

            foreach (var offset in _entityContainers)
            {
                var val = GetInventoryFromBlockPosition(center + offset);

                if (val.Item1 == null)
                    continue;

                lootWindowGroup.SetTileEntityChest("QUICKSTACK", val.Item1);
                PackedBoolArray lockedSlots = new PackedBoolArray(lootWindowGroup.lootWindow.lootContainer.items.Length);
                XUiM_LootContainer.StashItems(backpackWindow, lootWindowGroup.lootWindow.lootContainer, playerUI.mXUi.PlayerInventory, 0, lockedSlots, moveKind, playerControls.MoveStartBottomRight);
                val.Item2.SetModified();
            }
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
        }
    }

    // UI Delegates
    public static void QuickStackOnClick()
    {
        try
        {
            // Singleplayer
            if (ConnectionManager.Instance.IsSinglePlayer)
            {
                MoveQuickStack();
                // Multiplayer (Client)
            }
            else if (!ConnectionManager.Instance.IsServer)
            {
                ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageFindOpenableContainers>().Setup(GameManager.Instance.World.GetPrimaryPlayerId(), QuickStackType.Stack));
                // Multiplayer (Host)
            }
            else if (!GameManager.IsDedicatedServer)
            {
                // But we do the steps of Multiplayer quick stack in-place because
                // The host has access to locking functions
                var player = GameManager.Instance.World.GetPrimaryPlayer();
                var center = new Vector3i(player.position);
                List<Vector3i> offsets = new List<Vector3i>(1024);
                foreach (var pair in FindNearbyLootContainers(center, player.entityId))
                {
                    offsets.Add(pair.Item1);
                }
                ClientMoveQuickStack(center, offsets);
            }
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
        }
    }

    public static void QuickRestockOnClick()
    {
        try
        {
            // Singleplayer
            if (ConnectionManager.Instance.IsSinglePlayer)
            {
                MoveQuickRestock();
                // Multiplayer (Client)
            }
            else if (!ConnectionManager.Instance.IsServer)
            {
                ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageFindOpenableContainers>().Setup(GameManager.Instance.World.GetPrimaryPlayerId(), QuickStackType.Restock));
                // Multiplayer (Host)
            }
            else if (!GameManager.IsDedicatedServer)
            {
                // TODO: could be cleaned up a bit...
                // But we do the steps of Multiplayer quick stack in-place because
                // The host has access to locking functions
                var player = GameManager.Instance.World.GetPrimaryPlayer();
                var center = new Vector3i(player.position);
                List<Vector3i> offsets = new List<Vector3i>(1024);
                foreach (var pair in FindNearbyLootContainers(center, player.entityId))
                {
                    offsets.Add(pair.Item1);
                }
                ClientMoveQuickRestock(center, offsets);
            }
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
        }
    }

    public static void DumpVehicleDownOnClick()
    {
        //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleDownOnClick START");

        bool isDedicated = GameManager.IsDedicatedServer;
        bool isClient = SingletonMonoBehaviour<ConnectionManager>.Instance.IsClient;
        bool isSinglePlayer = SingletonMonoBehaviour<ConnectionManager>.Instance.IsSinglePlayer;
        bool isServer = ConnectionManager.Instance.IsServer;

        EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();

        if (primaryPlayer != null)
        {
            if (isSinglePlayer)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleDownOnClick NOT Client");
                DumpVehicleDown(primaryPlayer);
            }
            else if (isClient)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleDownOnClick IS Client");
                DumpVehicleDown(primaryPlayer, true);
            }
            else if (isServer && !isDedicated)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleDownOnClick IS P2P Host");
                DumpVehicleDown(primaryPlayer, true);
            }
        }
    }

    public static void DumpVehicleUpOnClick()
    {
        //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleUpOnClick START");

        bool isDedicated = GameManager.IsDedicatedServer;
        bool isClient = SingletonMonoBehaviour<ConnectionManager>.Instance.IsClient;
        bool isSinglePlayer = SingletonMonoBehaviour<ConnectionManager>.Instance.IsSinglePlayer;
        bool isServer = ConnectionManager.Instance.IsServer;

        EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();

        if (primaryPlayer != null)
        {
            if (isSinglePlayer)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleUpOnClick NOT Client");
                DumpVehicleUp(primaryPlayer);
            }
            else if (isClient)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleUpOnClick IS Client");
                DumpVehicleUp(primaryPlayer, true);
            }
            else if (isServer && !isDedicated)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleUpOnClick IS P2P Host");
                DumpVehicleUp(primaryPlayer, true);
            }
        }
    }

    public static void DumpDroneDownOnClick()
    {
        //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleDownOnClick START");

        bool isDedicated = GameManager.IsDedicatedServer;
        bool isClient = SingletonMonoBehaviour<ConnectionManager>.Instance.IsClient;
        bool isSinglePlayer = SingletonMonoBehaviour<ConnectionManager>.Instance.IsSinglePlayer;
        bool isServer = ConnectionManager.Instance.IsServer;

        EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();

        if (primaryPlayer != null)
        {
            if (isSinglePlayer)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleDownOnClick NOT Client");
                DumpDroneDown(primaryPlayer);
            }
            else if (isClient)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleDownOnClick IS Client");
                DumpDroneDown(primaryPlayer, true);
            }
            else if (isServer && !isDedicated)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleDownOnClick IS P2P Host");
                DumpDroneDown(primaryPlayer, true);
            }
        }
    }

    public static void DumpDroneUpOnClick()
    {
        //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleUpOnClick START");

        bool isDedicated = GameManager.IsDedicatedServer;
        bool isClient = SingletonMonoBehaviour<ConnectionManager>.Instance.IsClient;
        bool isSinglePlayer = SingletonMonoBehaviour<ConnectionManager>.Instance.IsSinglePlayer;
        bool isServer = ConnectionManager.Instance.IsServer;

        EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();

        if (primaryPlayer != null)
        {
            if (isSinglePlayer)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleUpOnClick NOT Client");
                DumpDroneUp(primaryPlayer);
            }
            else if (isClient)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleUpOnClick IS Client");
                DumpDroneUp(primaryPlayer, true);
            }
            else if (isServer && !isDedicated)
            {
                //Log.Out("XUiC_ContainerStandardControlsPatches-DumpVehicleUpOnClick IS P2P Host");
                DumpDroneUp(primaryPlayer, true);
            }
        }
    }


    public static void UpdateUI()
    {
        try
        {
            if (playerBackpack == null)
                return;

            XUiController[] slots = playerBackpack.GetItemStackControllers();

            for (int i = 0; i < slots.Length; ++i)
                (slots[i].GetChildById("iconSlotLock").ViewComponent as XUiV_Sprite).Color = lockIconColor;

            playerControls.GetChildById("btnToggleLockMode").ViewComponent.IsVisible = lockModeIconVisible;
        }
        catch (Exception e)
        {
            printExceptionInfo(e);
        }
    }

    public static void LoadConfig()
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            if (!File.Exists(configFilePath))
                throw new Exception($"Unable to find config at: {configFilePath}");

            XmlDocument xml = new XmlDocument();
            xml.Load(configFilePath);

            string[] quickLockButtons = xml.GetElementsByTagName("QuickLockButtons")[0].InnerText.Trim().Split(' ');
            if (quickLockButtons.Length == 0)
                throw new Exception("Must have at least one value for tag QuickLockButtons");
            quickLockHotkeys = new KeyCode[quickLockButtons.Length];
            for (int i = 0; i < quickLockButtons.Length; i++)
                quickLockHotkeys[i] = (KeyCode)int.Parse(quickLockButtons[i]);

            string[] quickStackButtons = xml.GetElementsByTagName("QuickStackButtons")[0].InnerText.Trim().Split(' ');
            if (quickStackButtons.Length == 0)
                throw new Exception("Must have at least one value for tag QuickStackButtons");
            quickStackHotkeys = new KeyCode[quickStackButtons.Length];
            for (int i = 0; i < quickStackButtons.Length; i++)
                quickStackHotkeys[i] = (KeyCode)int.Parse(quickStackButtons[i]);

            string[] quickRestockButtons = xml.GetElementsByTagName("QuickRestockButtons")[0].InnerText.Trim().Split(' ');
            if (quickRestockButtons.Length == 0)
                throw new Exception("Must have at least one value for tag QuickRestockButtons");
            quickRestockHotkeys = new KeyCode[quickRestockButtons.Length];
            for (int i = 0; i < quickRestockButtons.Length; i++)
                quickRestockHotkeys[i] = (KeyCode)int.Parse(quickRestockButtons[i]);

            lockModeIconVisible = Boolean.Parse(xml.GetElementsByTagName("LockModeIconVisible")[0].InnerText);

            string[] stashDistanceStr = xml.GetElementsByTagName("QuickStashDistance")[0].InnerText.Trim().Split(' ');
            if (stashDistanceStr.Length != 3)
                throw new Exception("Must have exactly three values for tag QuickStashDistance");
            stashDistance.x = Math.Min(Math.Max(int.Parse(stashDistanceStr[0]), 0), 127);
            stashDistance.y = Math.Min(Math.Max(int.Parse(stashDistanceStr[1]), 0), 127);
            stashDistance.z = Math.Min(Math.Max(int.Parse(stashDistanceStr[2]), 0), 127);

            string[] iconColor = xml.GetElementsByTagName("LockedSlotsIconColor")[0].InnerText.Trim().Split(' ');
            if (iconColor.Length != 4)
                throw new Exception("Must have exactly four values for tag LockedSlotsIconColor");
            lockIconColor = new Color32(Byte.Parse(iconColor[0]), Byte.Parse(iconColor[1]), Byte.Parse(iconColor[2]), Byte.Parse(iconColor[3]));

            string[] borderColor = xml.GetElementsByTagName("LockedSlotsBorderColor")[0].InnerText.Trim().Split(' ');
            if (borderColor.Length != 4)
                throw new Exception("Must have exactly four values for tag LockedSlotsBorderColor");
            lockBorderColor = new Color32(Byte.Parse(borderColor[0]), Byte.Parse(borderColor[1]), Byte.Parse(borderColor[2]), Byte.Parse(borderColor[3]));

            UpdateUI();

            Log.Out($"[QuickStack] Loaded config in {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            quickLockHotkeys = new KeyCode[1];
            quickLockHotkeys[0] = KeyCode.LeftAlt;

            quickStackHotkeys = new KeyCode[2];
            quickStackHotkeys[0] = KeyCode.LeftAlt;
            quickStackHotkeys[1] = KeyCode.X;

            quickRestockHotkeys = new KeyCode[2];
            quickRestockHotkeys[0] = KeyCode.LeftAlt;
            quickRestockHotkeys[1] = KeyCode.Z;

            lockModeIconVisible = true;

            stashDistance = new Vector3i(7, 7, 7);

            lockIconColor = new Color32(255, 0, 0, 255);
            lockBorderColor = new Color32(128, 0, 0, 0);

            Log.Warning("[QuickStack] Failed to load or parse config");
            printExceptionInfo(e);
        }
    }

    private static void DumpVehicleDown(EntityPlayer primaryPlayer, bool sendToServer = false)
    {
        //Log.Out("RebirthUtilities-DumpVehicleDown NOT isClient");

        if (primaryPlayer != null)
        {

            World _world = GameManager.Instance.World;

            int minMax = 50;
            bool addedItems = false;

            List<Entity> entitiesInBounds = _world.GetEntitiesInBounds(typeof(EntityVehicle), BoundsUtils.BoundsForMinMax(primaryPlayer.position.x - minMax, primaryPlayer.position.y - 50, primaryPlayer.position.z - minMax, primaryPlayer.position.x + minMax, primaryPlayer.position.y + 30, primaryPlayer.position.z + minMax), new List<Entity>());


            if (entitiesInBounds.Count > 0)
            {
                PackedBoolArray lockedSlots = primaryPlayer.bag.LockedSlots;

                for (int x = 0; x < entitiesInBounds.Count; x++)
                {
                    EntityVehicle entityVehicle = (EntityVehicle)entitiesInBounds[x];

                    if (entityVehicle != null)
                    {

                        PersistentPlayerData playerData = _world.GetGameManager().GetPersistentPlayerList().GetPlayerData(entityVehicle.GetOwner());

                        if (playerData.EntityId == primaryPlayer.entityId)
                        {
                            ItemStack[] slots = primaryPlayer.bag.GetSlots();
                            for (int i = 0; i < slots.Length; i++)
                            {
                                ItemStack itemStack = slots[i];

                                bool skipSlot = false;

                                if (lockedSlots != null)
                                {
                                    skipSlot = lockedSlots[i];
                                }

                                if (!itemStack.IsEmpty() && !skipSlot)
                                {

                                    string itemName = itemStack.itemValue.ItemClass.GetItemName().ToLower();

                                    if (itemName != "questwhiteriversupplies" &&
                                        itemName != "questcassadoresupplies"
                                        )
                                    {
                                        if (entityVehicle.bag.HasItem(itemStack.itemValue))
                                        {
                                            ValueTuple<bool, bool> tryStack = entityVehicle.bag.TryStackItem(0, itemStack);

                                            bool item = tryStack.Item1;
                                            bool item2 = tryStack.Item2;

                                            if (!tryStack.Item1)
                                            {
                                                bool tryAdd = entityVehicle.bag.AddItem(itemStack);
                                                if (tryAdd)
                                                {
                                                    primaryPlayer.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                    addedItems = true;
                                                }
                                            }
                                            else
                                            {
                                                if (tryStack.Item2)
                                                {
                                                    primaryPlayer.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                    addedItems = true;
                                                }
                                                else
                                                {
                                                    primaryPlayer.bag.SetSlot(i, itemStack.Clone());
                                                    addedItems = true;
                                                    bool tryAdd = entityVehicle.bag.AddItem(itemStack);
                                                    if (tryAdd)
                                                    {
                                                        primaryPlayer.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                        addedItems = true;
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            bool tryAdd = entityVehicle.bag.AddItem(itemStack);
                                            if (tryAdd)
                                            {
                                                primaryPlayer.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                addedItems = true;
                                            }
                                        }
                                    }
                                }
                            }

                            ItemStack[] slotsVehicle = entityVehicle.bag.GetSlots();

                            if (sendToServer)
                            {
                                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageVehicleUpdateBag>().Setup(entityVehicle.entityId, slotsVehicle));
                            }

                            break;
                        }
                    }
                }
            }
        }
    }

    public static void DumpVehicleUp(EntityPlayer primaryPlayer, bool sendToServer = false)
    {

        if (primaryPlayer != null)
        {

            World _world = GameManager.Instance.World;

            int minMax = 50;
            bool addedItems = false;

            List<Entity> entitiesInBounds = _world.GetEntitiesInBounds(
                typeof(EntityVehicle),
                BoundsUtils.BoundsForMinMax(
                    primaryPlayer.position.x - minMax, primaryPlayer.position.y - 50, primaryPlayer.position.z - minMax,
                    primaryPlayer.position.x + minMax, primaryPlayer.position.y + 30, primaryPlayer.position.z + minMax
                ),
                new List<Entity>());


            if (entitiesInBounds.Count > 0)
            {
                for (int x = 0; x < entitiesInBounds.Count; x++)
                {
                    EntityVehicle entityVehicle = (EntityVehicle)entitiesInBounds[x];

                    if (entityVehicle != null)
                    {

                        PersistentPlayerData playerData = _world.GetGameManager().GetPersistentPlayerList().GetPlayerData(entityVehicle.GetOwner());

                        if (playerData.EntityId == primaryPlayer.entityId)
                        {
                            ItemStack[] slots = entityVehicle.bag.GetSlots();

                            // NEW: get vehicle’s locked slots
                            PackedBoolArray vehicleLockedSlots = entityVehicle.bag.LockedSlots;

                            for (int i = 0; i < slots.Length; i++)
                            {
                                // skip locked vehicle slots
                                bool skipVehicleSlot = false;
                                if (vehicleLockedSlots != null)
                                {
                                    skipVehicleSlot = vehicleLockedSlots[i];
                                }
                                if (skipVehicleSlot)
                                {
                                    continue;
                                }

                                ItemStack itemStack = slots[i];

                                if (!itemStack.IsEmpty())
                                {

                                    if (primaryPlayer.bag.HasItem(itemStack.itemValue))
                                    {
                                        ValueTuple<bool, bool> tryStack = primaryPlayer.bag.TryStackItem(0, itemStack);

                                        bool item = tryStack.Item1;
                                        bool item2 = tryStack.Item2;

                                        if (!tryStack.Item1)
                                        {
                                            bool tryAdd = primaryPlayer.bag.AddItem(itemStack);
                                            if (tryAdd)
                                            {
                                                entityVehicle.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                addedItems = true;
                                            }
                                        }
                                        else
                                        {
                                            if (tryStack.Item2)
                                            {
                                                entityVehicle.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                addedItems = true;
                                            }
                                            else
                                            {
                                                entityVehicle.bag.SetSlot(i, itemStack.Clone());
                                                addedItems = true;
                                                bool tryAdd = primaryPlayer.bag.AddItem(itemStack);
                                                if (tryAdd)
                                                {
                                                    entityVehicle.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                    addedItems = true;
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        bool tryAdd = primaryPlayer.bag.AddItem(itemStack);
                                        if (tryAdd)
                                        {
                                            entityVehicle.bag.SetSlot(i, ItemStack.Empty.Clone());
                                            addedItems = true;
                                        }
                                    }
                                }
                            }

                            ItemStack[] slotsVehicle = entityVehicle.bag.GetSlots();
                            for (int n = 0; n < slotsVehicle.Length; n++)
                            {
                                if (!slotsVehicle[n].IsEmpty())
                                {
                                    //Log.Out("RebirthUtilities-DumpVehicleUp B Bag [" + n + "]: " + slotsVehicle[n].itemValue.ItemClass.GetItemName());
                                }
                            }

                            if (sendToServer)
                            {
                                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                                    NetPackageManager.GetPackage<NetPackageVehicleUpdateBag>().Setup(entityVehicle.entityId, slotsVehicle));
                            }
                        }
                    }
                }
            }
        }
    }

    private static void DumpDroneDown(EntityPlayer primaryPlayer, bool sendToServer = false)
    {
        //Log.Out("RebirthUtilities-DumpVehicleDown NOT isClient");

        if (primaryPlayer != null)
        {

            World _world = GameManager.Instance.World;

            int minMax = 50;
            bool addedItems = false;

            List<Entity> entitiesInBounds = _world.GetEntitiesInBounds(typeof(EntityDrone), BoundsUtils.BoundsForMinMax(primaryPlayer.position.x - minMax, primaryPlayer.position.y - 50, primaryPlayer.position.z - minMax, primaryPlayer.position.x + minMax, primaryPlayer.position.y + 30, primaryPlayer.position.z + minMax), new List<Entity>());


            if (entitiesInBounds.Count > 0)
            {
                PackedBoolArray lockedSlots = primaryPlayer.bag.LockedSlots;

                for (int x = 0; x < entitiesInBounds.Count; x++)
                {
                    EntityDrone entityDrone = (EntityDrone)entitiesInBounds[x];

                    if (entityDrone != null)
                    {

                        PersistentPlayerData playerData = _world.GetGameManager().GetPersistentPlayerList().GetPlayerData(entityDrone.GetOwner());

                        if (playerData.EntityId == primaryPlayer.entityId)
                        {
                            ItemStack[] slots = primaryPlayer.bag.GetSlots();
                            for (int i = 0; i < slots.Length; i++)
                            {
                                ItemStack itemStack = slots[i];

                                bool skipSlot = false;

                                if (lockedSlots != null)
                                {
                                    skipSlot = lockedSlots[i];
                                }

                                if (!itemStack.IsEmpty() && !skipSlot)
                                {

                                    string itemName = itemStack.itemValue.ItemClass.GetItemName().ToLower();

                                    if (itemName != "questwhiteriversupplies" &&
                                        itemName != "questcassadoresupplies"
                                        )
                                    {
                                        if (entityDrone.bag.HasItem(itemStack.itemValue))
                                        {
                                            ValueTuple<bool, bool> tryStack = entityDrone.bag.TryStackItem(0, itemStack);

                                            bool item = tryStack.Item1;
                                            bool item2 = tryStack.Item2;

                                            if (!tryStack.Item1)
                                            {
                                                bool tryAdd = entityDrone.bag.AddItem(itemStack);
                                                if (tryAdd)
                                                {
                                                    primaryPlayer.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                    addedItems = true;
                                                }
                                            }
                                            else
                                            {
                                                if (tryStack.Item2)
                                                {
                                                    primaryPlayer.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                    addedItems = true;
                                                }
                                                else
                                                {
                                                    primaryPlayer.bag.SetSlot(i, itemStack.Clone());
                                                    addedItems = true;
                                                    bool tryAdd = entityDrone.bag.AddItem(itemStack);
                                                    if (tryAdd)
                                                    {
                                                        primaryPlayer.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                        addedItems = true;
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            bool tryAdd = entityDrone.bag.AddItem(itemStack);
                                            if (tryAdd)
                                            {
                                                primaryPlayer.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                addedItems = true;
                                            }
                                        }
                                    }
                                }
                            }

                            ItemStack[] slotsVehicle = entityDrone.bag.GetSlots();

                            if (sendToServer)
                            {
                                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageVehicleUpdateBag>().Setup(entityDrone.entityId, slotsVehicle));
                            }
                        }
                    }
                }
            }
        }
    }

    public static void DumpDroneUp(EntityPlayer primaryPlayer, bool sendToServer = false)
    {

        if (primaryPlayer != null)
        {

            World _world = GameManager.Instance.World;

            int minMax = 50;
            bool addedItems = false;

            List<Entity> entitiesInBounds = _world.GetEntitiesInBounds(
                typeof(EntityDrone),
                BoundsUtils.BoundsForMinMax(
                    primaryPlayer.position.x - minMax, primaryPlayer.position.y - 50, primaryPlayer.position.z - minMax,
                    primaryPlayer.position.x + minMax, primaryPlayer.position.y + 30, primaryPlayer.position.z + minMax
                ),
                new List<Entity>());


            if (entitiesInBounds.Count > 0)
            {
                for (int x = 0; x < entitiesInBounds.Count; x++)
                {
                    EntityDrone entityDrone = (EntityDrone)entitiesInBounds[x];

                    if (entityDrone != null)
                    {

                        PersistentPlayerData playerData = _world.GetGameManager().GetPersistentPlayerList().GetPlayerData(entityDrone.GetOwner());

                        if (playerData.EntityId == primaryPlayer.entityId)
                        {
                            ItemStack[] slots = entityDrone.bag.GetSlots();
                            Log.Out("Drone Slots: " + slots.Length);

                            // NEW: get vehicle’s locked slots
                            PackedBoolArray vehicleLockedSlots = entityDrone.bag.LockedSlots;
                            if (vehicleLockedSlots != null)
                            {
                                Log.Out("Drone Locked Slots: " + vehicleLockedSlots.Length);
                            }

                            for (int i = 0; i < slots.Length; i++)
                            {
                                // skip locked vehicle slots
                                bool skipVehicleSlot = false;
                                if (vehicleLockedSlots != null)
                                {
                                    skipVehicleSlot = vehicleLockedSlots[i];
                                }
                                if (skipVehicleSlot)
                                {
                                    continue;
                                }

                                ItemStack itemStack = slots[i];

                                if (!itemStack.IsEmpty())
                                {

                                    if (primaryPlayer.bag.HasItem(itemStack.itemValue))
                                    {
                                        ValueTuple<bool, bool> tryStack = primaryPlayer.bag.TryStackItem(0, itemStack);

                                        bool item = tryStack.Item1;
                                        bool item2 = tryStack.Item2;

                                        if (!tryStack.Item1)
                                        {
                                            bool tryAdd = primaryPlayer.bag.AddItem(itemStack);
                                            if (tryAdd)
                                            {
                                                entityDrone.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                addedItems = true;
                                            }
                                        }
                                        else
                                        {
                                            if (tryStack.Item2)
                                            {
                                                entityDrone.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                addedItems = true;
                                            }
                                            else
                                            {
                                                entityDrone.bag.SetSlot(i, itemStack.Clone());
                                                addedItems = true;
                                                bool tryAdd = primaryPlayer.bag.AddItem(itemStack);
                                                if (tryAdd)
                                                {
                                                    entityDrone.bag.SetSlot(i, ItemStack.Empty.Clone());
                                                    addedItems = true;
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        bool tryAdd = primaryPlayer.bag.AddItem(itemStack);
                                        if (tryAdd)
                                        {
                                            entityDrone.bag.SetSlot(i, ItemStack.Empty.Clone());
                                            addedItems = true;
                                        }
                                    }
                                }
                            }

                            ItemStack[] slotsVehicle = entityDrone.bag.GetSlots();
                            for (int n = 0; n < slotsVehicle.Length; n++)
                            {
                                if (!slotsVehicle[n].IsEmpty())
                                {
                                    //Log.Out("RebirthUtilities-DumpVehicleUp B Bag [" + n + "]: " + slotsVehicle[n].itemValue.ItemClass.GetItemName());
                                }
                            }

                            if (sendToServer)
                            {
                                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                                    NetPackageManager.GetPackage<NetPackageVehicleUpdateBag>().Setup(entityDrone.entityId, slotsVehicle));
                            }
                        }
                    }
                }
            }
        }
    }
}
