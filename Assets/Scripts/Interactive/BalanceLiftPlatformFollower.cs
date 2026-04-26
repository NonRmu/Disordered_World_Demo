using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class BalanceLiftPlatformFollower : MonoBehaviour
{
    private readonly HashSet<Transform> occupants = new HashSet<Transform>();
    private Vector3 lastPosition;

    private void Start()
    {
        lastPosition = transform.position;
    }

    private void LateUpdate()
    {
        Vector3 delta = transform.position - lastPosition;

        if (delta.sqrMagnitude <= 0.0000001f)
        {
            lastPosition = transform.position;
            return;
        }

        foreach (Transform t in occupants)
        {
            if (t == null)
                continue;

            Rigidbody rb = t.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.MovePosition(rb.position + delta);
            }
            else
            {
                t.position += delta;
            }
        }

        lastPosition = transform.position;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.transform != transform)
            occupants.Add(collision.transform);
    }

    private void OnCollisionExit(Collision collision)
    {
        occupants.Remove(collision.transform);
    }
}