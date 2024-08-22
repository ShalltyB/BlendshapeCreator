#if !HS2
using HarmonyLib;


namespace BlendshapeCreator
{
    
    class Hooks
    {
        [HarmonyPostfix, HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeCoordinateType), typeof(ChaFileDefine.CoordinateType), typeof(bool))]
        private static void PostfixChangeCoordinate(ChaControl __instance)
        {
            var controller = __instance.gameObject.GetComponent<CharacterController>();
            if (controller != null)
                controller.CoordintateChangeEvent();
        }
    }
}
#endif
