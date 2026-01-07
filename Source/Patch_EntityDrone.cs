using HarmonyLib;
using System;
using UnityEngine.Windows;

[HarmonyPatch(typeof(EntityDrone), nameof(EntityDrone.LoadMods))]
public class Patch_GameManager_Update
{
    static void Postfix(EntityDrone __instance)
    {
        __instance.bag.LockedSlots = __instance.lootContainer.SlotLocks;
    }
}