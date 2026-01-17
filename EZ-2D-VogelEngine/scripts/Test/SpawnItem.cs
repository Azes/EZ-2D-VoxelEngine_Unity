using UnityEngine;
using UnityEngine.InputSystem;

public class SpawnItem : MonoBehaviour
{

    public GameObject spawnItem;
    public Vector2 ForceToAdd;


    // Update is called once per frame
    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            var pos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            pos.z = 0;
            var item = Instantiate(spawnItem, pos, Quaternion.identity);
            var ve = item.GetComponent<VoxelEntity>();
            ve.AddForce(ForceToAdd);
            Debug.Log("Spawn");
        }        
    }
}
