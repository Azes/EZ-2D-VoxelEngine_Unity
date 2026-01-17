using UnityEngine;

public class VoxelEntity : MonoBehaviour
{

    public float Gravity;
    public float AirDrag = 6f;
    public float GroundDrag = 20f;

    [HideInInspector] public Vector2 velocity;
    public bool groundet;


    private void FixedUpdate()
    {

        if (groundet)
            velocity.y = Mathf.Max(velocity.y, -.01f);
        else
            velocity.y -= Gravity * Time.fixedDeltaTime;

        float drag = groundet ? GroundDrag : AirDrag;

        velocity.x = Mathf.MoveTowards(
            velocity.x,
            0f,
            drag * Time.fixedDeltaTime
        );

        CollisionChecks();

        transform.position += (Vector3)velocity * Time.fixedDeltaTime;
    }
    public void AddForce(Vector2 force)
    {
        velocity += force;
    }

    private void CollisionChecks()
    {
        float width = transform.localScale.x;
        float height = transform.localScale.y;
        float px = transform.position.x;
        float py = Mathf.RoundToInt(transform.position.y);
        float skinWidth = 0.02f;

        bool solid(int x, int y) => WorldGen.isSolidBlock(x, y);

        int footY = Mathf.FloorToInt(py - skinWidth);

        int Grounded = 0;


        for (int sx = 0; sx <= width; sx++)
            if (solid(Mathf.FloorToInt(px + sx), footY))
                Grounded++;

        if (Grounded > 0 && velocity.y <= 0)
        {
            velocity.y = 0;

            var t = transform.position;
            t.y = Mathf.RoundToInt(t.y);
            transform.position = t;

            groundet = true;
        }
        else
        {
            groundet = false;
        }


        // LEFT collider
        int leftX = Mathf.FloorToInt(px - skinWidth);
        int lc = 0;

        for (int sy = 0; sy < height; sy++)
            if (solid(leftX, Mathf.FloorToInt(py + sy + skinWidth)))
                lc++;

        if (velocity.x < 0 && lc > 0)
        {
            var t = transform.position;
            t.x = (float)leftX + 1 + skinWidth;
            transform.position = t;
            velocity.x = 0;
        }


        // RIGHT collider
        int rightX = Mathf.FloorToInt(px + width + skinWidth);
        int rc = 0;


        for (int sy = 0; sy < height; sy++)
            if (solid(rightX, Mathf.FloorToInt(py + sy + skinWidth)))
                rc++;

        if (velocity.x > 0 && rc > 0)
        {
            var t = transform.position;

            t.x = (float)rightX - width - skinWidth;
            transform.position = t;
            velocity.x = 0;
        }

        // HEAD collider
        int headY = Mathf.FloorToInt(py + height);
        int hc = 0;

        for (int sx = 0; sx <= width; sx++)
            if (solid(Mathf.FloorToInt(px + sx), headY))
                hc++;

        if (velocity.y > 0 && hc > 0)
        {
            var t = transform.position;

            t.y = (float)headY - height - skinWidth;
            transform.position = t;
            velocity.y = 0;
        }

    }

}
