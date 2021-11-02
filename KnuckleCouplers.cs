using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DV.CabControls;
using DV.CabControls.Spec;
using HarmonyLib;
using UnityEngine;

namespace DvMod.ZCouplers
{
    public static class KnuckleCouplers
    {
        public static readonly bool enabled = Main.settings.couplerType == CouplerType.JanneyKnuckle;
        private static readonly AssetBundle bundle = AssetBundle.LoadFromFile(Path.Combine(Main.mod!.Path, "knucklecoupler"));
        private static readonly GameObject hookPrefab = bundle.LoadAsset<GameObject>("hook");

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Enabled))]
        public static class Entry_EnabledPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                CreateHook(__instance.couplerAdapter.coupler);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Enabled))]
        public static class Exit_EnabledPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                GameObject.Destroy(GetPivot(__instance)?.gameObject);
            }
        }

        private static void YLookAt(Transform t1, Transform t2)
        {
            t1.localEulerAngles = Vector3.zero;
            var offset = t1.InverseTransformPoint(t2.position);
            var angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            t1.localEulerAngles = new Vector3(0, angle, 0);
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Attached))]
        public static class Entry_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                var pivot = GetPivot(__instance);
                pivot!.GetComponentInChildren<ButtonBase>().Used = () => OnButtonPressed(__instance);
                pivot!.GetComponentInChildren<MeshCollider>().enabled = true;

                __instance.attachedTo = __instance.couplerAdapter.coupler.coupledTo.visualCoupler.chain.GetComponent<ChainCouplerInteraction>();
                __instance.attachedTo.attachedTo = __instance;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.LateUpdate_Attached))]
        public static class LateUpdate_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                var pivot = GetPivot(__instance);
                var otherPivot = GetPivot(__instance.attachedTo);
                if (pivot != null && otherPivot != null)
                {
                    YLookAt(pivot, otherPivot);
                    YLookAt(otherPivot, pivot);
                }
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Attached))]
        public static class Exit_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                var pivot = GetPivot(__instance);
                if (pivot != null)
                {
                    pivot.localEulerAngles = Vector3.zero;
                    pivot.GetComponentInChildren<MeshCollider>().enabled = false;
                }
                if (__instance.attachedTo != null)
                {
                    __instance.attachedTo.attachedTo = null;
                    __instance.attachedTo = null;
                }
            }
        }

        private static Transform? GetPivot(ChainCouplerInteraction chainScript)
        {
            if (chainScript == null)
                return null;
            var coupler = chainScript.couplerAdapter.coupler;
            if (coupler == null)
                return null;
            return coupler.transform.Find("ZCouplers pivot");
        }

        private static void SetMaterial(GameObject hook)
        {
            var material = TrainCar.Resolve(hook).transform.Find("[buffers]/HookPlate_F").GetComponent<MeshRenderer>().material;
            hook.GetComponent<MeshRenderer>().material = material;
        }

        private const float PivotLength = 1.0f;
        private const float HeightOffset = -0.067f;
        private static void CreateHook(Coupler coupler)
        {
            var frontPivot = new GameObject("ZCouplers pivot");
            frontPivot.transform.SetParent(coupler.transform, false);
            frontPivot.transform.localPosition = new Vector3(0, HeightOffset, -PivotLength);

            var hook = GameObject.Instantiate(hookPrefab);
            hook.layer = LayerMask.NameToLayer("Interactable");
            hook.transform.SetParent(frontPivot.transform, false);
            hook.transform.localPosition = PivotLength * Vector3.forward;
            SetMaterial(hook);

            var collider = hook.GetComponent<MeshCollider>();
            collider.convex = true;
            collider.enabled = false;
            collider.isTrigger = true;

            var buttonSpec = hook.AddComponent<Button>();
            buttonSpec.createRigidbody = false;
            buttonSpec.useJoints = false;
        }

        private static void OnButtonPressed(ChainCouplerInteraction chainScript)
        {
            chainScript.couplerAdapter.coupler.Uncouple(
                playAudio: true,
                calledOnOtherCoupler: false,
                dueToBrokenCouple: false,
                viaChainInteraction: true);
        }

        [HarmonyPatch(typeof(ChainCouplerVisibilityOptimizer), nameof(ChainCouplerVisibilityOptimizer.Enable))]
        public static class ChainCouplerVisibilityOptimizerEnablePatch
        {
            public static void Postfix(ChainCouplerVisibilityOptimizer __instance)
            {
                if (!enabled)
                    return;
                var chainTransform = __instance.chain.transform;
                for (int i = 0; i < chainTransform.childCount; i++)
                    chainTransform.GetChild(i).gameObject.SetActive(false);
            }
        }

        [HarmonyPatch]
        public static class ChainCouplerInteractionSkipPatches
        {
            private static readonly string[] MethodNames = new string[]
            {
                nameof(ChainCouplerInteraction.Entry_Enabled),
                nameof(ChainCouplerInteraction.Exit_Enabled),

                nameof(ChainCouplerInteraction.Entry_Attached),
                nameof(ChainCouplerInteraction.Exit_Attached),
                nameof(ChainCouplerInteraction.LateUpdate_Attached),

                nameof(ChainCouplerInteraction.Entry_Attached_Tight),
                nameof(ChainCouplerInteraction.Exit_Attached_Tight),

                nameof(ChainCouplerInteraction.Entry_Parked),
                nameof(ChainCouplerInteraction.Exit_Parked),
                nameof(ChainCouplerInteraction.Update_Parked),
            };
            public static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var name in MethodNames)
                    yield return AccessTools.Method(typeof(ChainCouplerInteraction), name);
            }

            public static bool Prefix()
            {
                return !enabled;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.DetermineNextState))]
        public static class DetermineNextStatePatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance, ref ChainCouplerInteraction.State __result)
            {
                if (!enabled)
                    return true;

                if (__instance.couplerAdapter.IsCoupled())
                {
                    var partner = __instance.couplerAdapter.coupler?.coupledTo?.visualCoupler?.chain?.GetComponent<ChainCouplerInteraction>();
                    if (partner == null)
                    {
                        __result = ChainCouplerInteraction.State.Disabled;
                        return false;
                    }
                    __result = ChainCouplerInteraction.State.Attached_Tight;
                }
                else
                {
                    __result = ChainCouplerInteraction.State.Parked;
                }

                return false;
            }
        }
    }
}