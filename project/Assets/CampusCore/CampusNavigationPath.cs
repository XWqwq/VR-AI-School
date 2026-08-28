using UnityEngine;

public sealed class CampusNavigationPath : MonoBehaviour
{
    public string targetLocationId;
    public Transform[] waypoints;

    public Vector3[] GetWorldPoints(Vector3 fallbackTarget)
    {
        if (waypoints == null || waypoints.Length == 0)
            return new[] { fallbackTarget };
        var points = new Vector3[waypoints.Length + 1];
        for (var i = 0; i < waypoints.Length; i++)
            points[i] = waypoints[i] != null ? waypoints[i].position : fallbackTarget;
        points[points.Length - 1] = fallbackTarget;
        return points;
    }
}
