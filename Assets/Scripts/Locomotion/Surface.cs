namespace GorillaLocomotion
{
    using UnityEngine;

    public class Surface : MonoBehaviour
    {
        public float slipPercentage;

        // [local] множитель отскока (батуты): 1 = обычная поверхность, 2+ = пружинит
        public float bounceMultiplier = 1f;
    }
}