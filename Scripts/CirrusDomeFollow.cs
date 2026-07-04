using UnityEngine;

public class CirrusDomeFollow : MonoBehaviour
{
    //Follow player to keep Cirrus clouds at same distance
    public Transform Target;

    private void Start()
    {
        if (Target == null && Camera.main != null)
            Target = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (Target == null) return;
        transform.position = Target.position;
    }
}
