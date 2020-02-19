﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Raycasting;

public class Spider : MonoBehaviour {

    private Rigidbody rb;

    [Header("Debug")]
    public bool showDebug;

    [Header("Scale of Transform")]
    public float scale = 1.0f;

    [Header("Movement")]
    public CapsuleCollider col;
    [Range(1, 10)]
    public float walkSpeed;
    [Range(1, 5)]
    public float turnSpeed;
    [Range(1, 10)]
    public float normalAdjustSpeed;
    public LayerMask walkableLayer;

    [Header("IK Legs")]
    public Transform body;
    public IKChain[] legs;
    public bool deactivateLegCentroidAdjustment;
    public bool deactivateLegNormalAdjustment;
    private Vector3 bodyNormal;

    [Header("Breathing")]
    public bool deactivateBreathing;
    [Range(1, 10)]
    public float timeForOneBreathCycle;
    [Range(0, 1)]
    public float breatheMagnitude;

    [Header("Ray Adjustments")]
    [Range(0.0f, 1.0f)]
    public float forwardRayLength;
    [Range(0.0f, 1.0f)]
    public float downRayLength;
    [Range(0.1f, 1.0f)]
    public float forwardRaySize = 0.66f;
    [Range(0.1f, 1.0f)]
    public float downRaySize = 0.9f;

    private Vector3 currentVelocity;
    private Vector3 lastNormal;
    private float gravityOffDist = 0.05f;

    private SphereCast downRay, forwardRay;
    private RaycastHit hitInfo;

    private struct groundInfo {
        public bool isGrounded;
        public Vector3 groundNormal;
        public float distanceToGround;

        public groundInfo(bool isGrd, Vector3 normal, float dist) {
            isGrounded = isGrd;
            groundNormal = normal;
            distanceToGround = dist;
        }
    }

    private groundInfo grdInfo;

    private void Awake() {
        rb = GetComponent<Rigidbody>();
    }

    void Start() {
        downRay = new SphereCast(transform.position, -transform.up, scale * downRayLength, downRaySize * scale * col.radius, transform, transform);
        forwardRay = new SphereCast(transform.position, transform.forward, scale * forwardRayLength, forwardRaySize * scale * col.radius, transform, transform);
        bodyNormal = body.transform.InverseTransformDirection(transform.up);
    }

    void FixedUpdate() {
        //** Ground Check **//
        grdInfo = GroundCheckSphere();

        //** Rotation to normal **// 
        Vector3 slerpNormal = Vector3.Slerp(transform.up, grdInfo.groundNormal, normalAdjustSpeed * Time.fixedDeltaTime);
        Quaternion goalrotation = getLookRotation(transform.right, slerpNormal);

        // Save last Normal for access
        lastNormal = transform.up;

        //Apply the rotation to the spider
        transform.rotation = goalrotation;


        // Dont apply gravity if close enough to ground
        if (grdInfo.distanceToGround > gravityOffDist) {
            rb.AddForce(-grdInfo.groundNormal * 1000.0f * Time.fixedDeltaTime); //Important using the groundnormal and not the lerping currentnormal here!
        }
    }

    void Update() {
        //** Debug **//
        if (showDebug) drawDebug();

        if (!deactivateLegCentroidAdjustment) body.transform.position = getLegCentroid();

        if (!deactivateLegNormalAdjustment) {
            Vector3 defaultNormal = body.TransformDirection(bodyNormal);
            Vector3 newNormal = GetLegsPlaneNormal();
            body.transform.rotation = Quaternion.FromToRotation(defaultNormal, newNormal) * body.transform.rotation;
            Debug.DrawLine(transform.position, transform.position + 5.0f * defaultNormal, Color.blue);
        }

        if (!deactivateBreathing) breathe();

    }

    /*
     * Returns the rotation with specified right and up direction (right will be projected onto the plane given by up)
     */
    public Quaternion getLookRotation(Vector3 right, Vector3 up) {
        if (up == Vector3.zero || right == Vector3.zero) return Quaternion.identity;
        Vector3 projRight = Vector3.ProjectOnPlane(right, up);
        if (projRight == Vector3.zero) return Quaternion.identity;
        Vector3 forward = Vector3.Cross(projRight, up);
        return Quaternion.LookRotation(forward, up);
    }

    public void turn(Vector3 goalForward, float speed) {
        if (goalForward == Vector3.zero) return;
        goalForward = Vector3.ProjectOnPlane(goalForward, transform.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(goalForward, transform.up), 50.0f * turnSpeed * speed);
    }

    public void walk(Vector3 direction, float speed) {
        direction = direction.normalized;
        // Increase velocity as the direction and forward vector of spider get closer together
        float distance = Mathf.Pow(Mathf.Clamp(Vector3.Dot(direction, transform.forward), 0, 1), 4) * 0.1f * walkSpeed * speed * scale;
        //Make sure per frame we wont move more than our downsphereRay radius, or we might lose the floor.
        //It is advised to call this method every fixed frame since collision is calculated on a fixed frame basis.
        distance = Mathf.Clamp(distance, 0, 0.99f * downRaySize);
        currentVelocity = distance * direction;
        transform.position += currentVelocity;
    }

    private void breathe() {
        float t = (Time.time * 2 * Mathf.PI / timeForOneBreathCycle) % (2 * Mathf.PI);
        //Could use body.transform.up here if i ever tilt the body. But the local coordinate system of the body isnt so nice
        Vector3 breatheOffset = transform.up * (0.5f * breatheMagnitude + (Mathf.Sin(t) * 0.5f * breatheMagnitude)) * col.radius * scale;
        body.transform.position = transform.position + breatheOffset;
    }

    public Vector3 getCurrentVelocityPerSecond() {
        return currentVelocity / Time.fixedDeltaTime;
    }

    public Vector3 getCurrentVelocityPerFrame() {
        return currentVelocity;
    }

    public Vector3 getLastNormal() {
        return lastNormal;
    }

    //** Ground Check Methods **//
    private groundInfo GroundCheckSphere() {
        if (forwardRay.castRay(out hitInfo, walkableLayer)) {
            return new groundInfo(true, hitInfo.normal.normalized, Vector3.Distance(transform.TransformPoint(col.center), hitInfo.point) - scale * col.radius);
        }

        if (downRay.castRay(out hitInfo, walkableLayer)) {
            return new groundInfo(true, hitInfo.normal.normalized, Vector3.Distance(transform.TransformPoint(col.center), hitInfo.point) - scale * col.radius);
        }

        return new groundInfo(false, Vector3.up, float.PositiveInfinity);
    }

    //** IK-Chains (Legs) Methods **//

    // Calculate the centroid (center of gravity) given by all end effector positions of the legs
    private Vector3 getLegCentroid() {
        if (legs == null) {
            Debug.LogError("Cant calculate leg centroid, legs not assigned.");
            return transform.position;
        }

        Vector3 position = Vector3.zero;
        float weigth = 1 / (float)legs.Length;

        // Go through all the legs, rotate the normal by it's offset
        for (int i = 0; i < legs.Length; i++) {
            position += legs[i].getEndEffector().position * weigth;
        }
        return position;
    }

    // Calculate the normal of the plane defined by leg positions, so we know how to rotate the body
    private Vector3 GetLegsPlaneNormal() {
        if (legs == null) {
            Debug.LogError("Cant calculate normal, legs not assigned.");
            return transform.up;
        }

        // float legRotWeigth = 1.0f;
        //if (legRotWeigth <= 0f) return transform.up; 
        //float legWeight = 1f / Mathf.Lerp(legs.Length,1f, legRotWeigth); // ???

        Vector3 normal = transform.up;
        float legWeight = 1f / legs.Length;

        for (int i = 0; i < legs.Length; i++) {
            normal += legWeight * legs[i].getTarget().normal;
            //normal += legWeight * -legs[i].getEndEffector().transform.up; // The minus comes from the endeffectors local coordinate system being reversed
        }
        return normal;
    }

    //** Get Methods **//
    public CapsuleCollider getCapsuleCollider() {
        return col;
    }

    public Vector3 getGroundNormal() {
        return grdInfo.groundNormal;
    }

    //** Debug Methods **//
    private void drawDebug() {
        downRay.draw(Color.green);
        forwardRay.draw(Color.blue);
        Vector3 borderpoint = transform.TransformPoint(col.center) + col.radius * scale * -transform.up;
        Debug.DrawLine(borderpoint, borderpoint + gravityOffDist * -transform.up, Color.black);
        Debug.DrawLine(transform.position, transform.position + 0.3f * scale * transform.up, new Color(1, 0.5f, 0, 1));
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected() {

        if (!showDebug) return;
        if (UnityEditor.EditorApplication.isPlaying) return;
        if (!UnityEditor.Selection.Contains(transform.gameObject)) return;

        Awake();
        Start();
        drawDebug();
    }
#endif

}
