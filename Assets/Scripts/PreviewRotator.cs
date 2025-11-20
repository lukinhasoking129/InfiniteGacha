using UnityEngine;

public class PreviewRotator : MonoBehaviour
{
    // Campos compatíveis com o que seu outro código parece esperar
    public float speed = 30f;       // graus por segundo
    public Vector3 axis = Vector3.up;

    void Update()
    {
        transform.Rotate(axis.normalized * (speed * Time.deltaTime), Space.World);
    }
}