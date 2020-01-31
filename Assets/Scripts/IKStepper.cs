﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(IKChain))]
public class IKStepper : MonoBehaviour
{
    public SpiderController spidercontroller;
    public IKStepper asyncChain; // Implement this

    public bool showDebug;

    [Range(0.01f, 1.0f)]
    public float debugIconScale = 0.1f;

    [Range(1.0f, 2.0f)]
    public float velocityPrediction = 1.5f;

    [Range(0, 10.0f)]
    public float stepTime;

    [Range(0.0f, 10.0f)]
    public float stepHeight;

    public Vector3 focusPoint;

    public AnimationCurve stepAnimation;

    private IKChain ikChain;

    private bool isStepping = false;

    private float minDistance;
    private float maxDistance;
    private float height;

    private AHingeJoint rootJoint;
    private Vector3 rootPos;
    private Vector3 defaultPositionLocal;
    private Vector3 prediction;
    private Vector3 lastEndEffectorPos;


    // Start is called before the first frame update
    void Start()
    {
        ikChain = GetComponent<IKChain>();
        spidercontroller = ikChain.spiderController;
        rootJoint = ikChain.getRootJoint();
        rootPos = rootJoint.getRotationPoint();

        float chainLength = ikChain.getChainLength();
        maxDistance = chainLength;
        minDistance = 0.3f * chainLength;
        height = 2.0f;
        defaultPositionLocal = spidercontroller.transform.InverseTransformPoint(rootPos + (minDistance + 0.5f * (maxDistance - minDistance)) * rootJoint.getMidOrientation());
        defaultPositionLocal.y = -1.0f * 1.0f / spidercontroller.scale; // Because spider has a 20.0f reference scale
    }

    void Update()
    {
        rootPos = rootJoint.getRotationPoint();

        if (showDebug)
        {
            if (isStepping)
            {
                DebugShapes.DrawPoint(ikChain.getTarget().position, Color.cyan, debugIconScale, stepTime);
            }
            else
            {

                DebugShapes.DrawPoint(ikChain.getTarget().position, Color.cyan, debugIconScale);
            }
            DebugShapes.DrawPoint(spidercontroller.transform.TransformPoint(defaultPositionLocal), Color.magenta, debugIconScale);
            DebugShapes.DrawPoint(prediction, Color.black, debugIconScale);
            DebugShapes.DrawPoint(lastEndEffectorPos, Color.gray, debugIconScale);
            Debug.DrawLine(lastEndEffectorPos, prediction, Color.black);
        }
    }

    public bool checkValidTarget(TargetInfo target)
    {
        Vector3 toTarget = target.position - rootPos;

        // Calculate current distances
        Vector3 horiz = Vector3.ProjectOnPlane(toTarget, spidercontroller.transform.up);
        Vector3 vert = Vector3.Project(toTarget, spidercontroller.transform.up);
        float horizontalDistance = Vector3.Magnitude(horiz);
        float verticalDistance = Vector3.Magnitude(vert);

        if (showDebug)
        {
            DebugShapes.DrawScope(rootPos, Vector3.ProjectOnPlane(rootJoint.getMinOrientation(), spidercontroller.transform.up), Vector3.ProjectOnPlane(rootJoint.getMaxOrientation(), spidercontroller.transform.up), spidercontroller.transform.up, minDistance, maxDistance, height, 3, Color.red);
            Debug.DrawLine(rootPos, rootPos + horiz, Color.green);
            Debug.DrawLine(rootPos + horiz, rootPos + horiz + vert, Color.green);
        }

        //      if targetposition projected onto the plane the spider is standing on, is farther away from the root joint than the whole chainlength
        //OR    if targetposition is not within the valid angle scope of the root joint
        //      This can cause trouble if we want the leg to be able to move e.g. beneath the spider since the scope does not allow the target to be behind the rootjoint
        if (horizontalDistance > maxDistance)
        {
            Debug.Log("Target too far away too reach.");
            return false;
        }
        else if (horizontalDistance < minDistance)
        {
            Debug.Log("Target too close for comfort.");
            return false;
        }
        else if (verticalDistance > height)
        {
            Debug.Log("Target too high or low too reach.");
            return false;
        }
        else if (!rootJoint.isVectorWithinScope(toTarget))
        {
            Debug.Log("Target not in scope");
            return false;
        }

        return true;
    }

    /*
     * Calculates a new target using the endeffector Position and a default position defined in this class.
     * The new target position is a movement towards the default position but overshoots the default position using the velocity prediction
     */
    public TargetInfo calcNewTarget()
    {
        Debug.Log("I'm calculating a new target position.");

        Vector3 endeffectorPosition = ikChain.getEndEffector().position;
        Vector3 defaultPosition = spidercontroller.transform.TransformPoint(defaultPositionLocal); //This gives us the defaultPos in World Space
        Vector3 normal = spidercontroller.transform.up;

        //Now predict step target
        prediction = Vector3.Project(transform.position, normal) + Vector3.ProjectOnPlane(endeffectorPosition + (defaultPosition - endeffectorPosition) * velocityPrediction, normal);
        if (showDebug)
        {
            Debug.DrawLine(endeffectorPosition, prediction, Color.magenta, 1.0f);
        }
        lastEndEffectorPos = endeffectorPosition; //Update this so it can be debug drawn in update function



        //Now shoot a rays using the prediction to find an actual point on a surface.
        RaycastHit hitInfo;


        //Straight down through prediction point
        if (shootRay(prediction + height * normal, -normal, 2 * height, out hitInfo))
        {
            return new TargetInfo(hitInfo.point, hitInfo.normal);
        }

        // From focus point to prediction
        else if (shootRay(spidercontroller.transform.TransformPoint(focusPoint), prediction, out hitInfo))
        {
            Debug.Log("Shot Focus Ray");
            //Debug.Break();
            return new TargetInfo(hitInfo.point, hitInfo.normal);
        }

        // Straight down through default position
        else if (shootRay(defaultPosition + height * normal, -normal, 2 * height, out hitInfo))
        {
            Debug.Log("Shot from default");
            //Debug.Break();
            return new TargetInfo(hitInfo.point, hitInfo.normal);
        }

        // Return default position
        else
        {
            Debug.Log("Had to return default");
            //Debug.Break();
            return new TargetInfo(defaultPosition, normal);
        }
    }

    private bool shootRay(Vector3 origin, Vector3 end, out RaycastHit hitInfo)
    {
        Vector3 v = end - origin;
        return shootRay(origin, v.normalized, v.magnitude, out hitInfo);

    }

    private bool shootRay(Vector3 origin, Vector3 direction, float distance, out RaycastHit hitInfo)
    {
        direction = direction.normalized;

        if (showDebug)
        {
            Debug.DrawLine(origin, origin + distance * direction, Color.cyan, 1.0f);
        }

        if (Physics.Raycast(origin, direction, out hitInfo, distance, spidercontroller.groundedLayer, QueryTriggerInteraction.Ignore))
        {
            // could check if the normal is acceptable
            if (checkValidTarget(new TargetInfo(hitInfo.point, hitInfo.normal)))
            {
                return true;
            }
        }
        return false;
    }

    private bool shootSphere(Vector3 origin, Vector3 direction, float distance, float radius, out RaycastHit hitInfo)
    {
        direction = direction.normalized;

        if (showDebug)
        {
            DebugShapes.DrawSphere(origin, debugIconScale, 24, 12, Color.cyan);
            DebugShapes.DrawSphere(origin + distance / 2 * direction, debugIconScale, 24, 12, Color.cyan);
            DebugShapes.DrawSphere(origin + distance * direction, debugIconScale, 24, 12, Color.cyan);
        }

        if (Physics.SphereCast(origin, radius, direction, out hitInfo, distance, spidercontroller.groundedLayer, QueryTriggerInteraction.Ignore))
        {
            // could check if the normal is acceptable
            if (checkValidTarget(new TargetInfo(hitInfo.point, hitInfo.normal)))
            {
                return true;
            }
        }
        return false;
    }

    public void step(TargetInfo target)
    {
        if (isStepping)
        {
            return;
        }
        if (asyncChain != null && asyncChain.getIsStepping())
        {
            // Maybe do something here. Do i want a chain to potentialy have an invalid target for a while?
            return;
        }
        IEnumerator coroutineStepping = Step(target);
        StartCoroutine(coroutineStepping);
    }

    public bool getIsStepping()
    {
        return isStepping;
    }

    /*
     * Coroutine for stepping since i want to actually see the stepping process instead of it happening all within one frame
     * */
    private IEnumerator Step(TargetInfo newTarget)
    {
        Debug.Log("Stepping");
        // should probably move the targets while im moving so that i dont outrun them, however this is stupid because then im no longer at the surface point i want
        isStepping = true;
        TargetInfo lastTarget = ikChain.getTarget();
        TargetInfo lerpTarget;
        float time = 0;
        while (time < stepTime)
        {
            lerpTarget.position = Vector3.Lerp(lastTarget.position, newTarget.position, time / stepTime) + stepHeight * stepAnimation.Evaluate(time / stepTime) * spidercontroller.transform.up;
            lerpTarget.normal = Vector3.Lerp(lastTarget.normal, newTarget.normal, time / stepTime);

            time += Time.deltaTime;
            ikChain.setTarget(lerpTarget);
            yield return null;
        }
        ikChain.setTarget(newTarget);
        isStepping = false;
    }

    void OnDrawGizmosSelected()
    {
        if (!UnityEditor.Selection.Contains(transform.gameObject))
        {
            return;
        }

        DebugShapes.DrawPoint(spidercontroller.transform.TransformPoint(focusPoint), Color.green, debugIconScale);
    }
}
