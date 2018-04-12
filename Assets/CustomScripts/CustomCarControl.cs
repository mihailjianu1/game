using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;

using UnityStandardAssets.Vehicles.Car;

[RequireComponent(typeof (CarController))]
public class CustomCarControl : MonoBehaviour {
    public GameManager gm;
    public TrackController tc;
    public Rigidbody rb;
    public CarController m_Car;

    public float nitroFuel = 1; // amount of fuel left (in percentage of full tank)
    public float nitroForce = 100000; // nitro force applied per second
    public float nitroCost = 0.2f; // fuel burnt per second
    public float nitroRegen = 0.01f; // fuel regenerated per second
    public float fuelUsed = 0;

    public int updateEvery;
    public float obstacleDetectionRadius;

    const float NO_DETECTION = 10000;

    Text dataText, endLevelText, scriptText, timeText, floatText;

    bool waitingForCommands = false;
    PlayerCommands cmds;

    float top_speed = 0;
    int frames = 0;
    int lastUpdate = 0;
    float floatFadeAmount = 1;
    public float floatFadeFactor = 4; // reverse exponential fade
    public float floatFadeTime;

    float secondsStuck = 0; //time we've been stuck for so far
    public float stuckTimeout = 3; //number of seconds of immobility before respawning
    public float stuckTolerance = 0.5f; //distance per second that counts as being 'non-stuck'

    private void Start() {
        gm = GameObject.Find("GameManager").GetComponent<GameManager>();
        if(gm == null) {
            Debug.Log("Error: Got no gm!");
        }
        tc = GameObject.Find("TrackController").GetComponent<TrackController>();
        if(tc == null) {
            Debug.Log("Error: Got no tc!");
        }
        dataText = GameObject.Find("DataText").GetComponent<Text>();
        if(dataText == null) {
            Debug.Log("Error: Got no DataText!");
        }
        endLevelText = GameObject.Find("EndLevelText").GetComponent<Text>();
        if(endLevelText == null) {
            Debug.Log("Error: Got no EndLevelText!");
        }
        scriptText = GameObject.Find("ScriptText").GetComponent<Text>();
        if(scriptText == null) {
            Debug.Log("Error: Got no ScriptText!");
        }
        timeText = GameObject.Find("TimeText").GetComponent<Text>();
        if(timeText == null) {
            Debug.Log("Error: Got no TimeText!");
        }
        floatText = GameObject.Find("FloatText").GetComponent<Text>();
        if(floatText == null) {
            Debug.Log("Error: Got no FloatText!");
        }
    }

    private void Awake() {
        // get the car controller
        m_Car = GetComponent<CarController>();
    }

    private float CastRay(float angle, float distance) {
        RaycastHit hit;
        float detection = NO_DETECTION;
        Vector3 ray_direction = Quaternion.AngleAxis(angle, Vector3.up) * transform.forward;
        if (Physics.SphereCast(rb.position, obstacleDetectionRadius, ray_direction, out hit, distance, -1, QueryTriggerInteraction.Ignore)) {
            detection = Math.Min(detection, hit.distance);
        }
        if (Physics.Raycast(rb.position, ray_direction, out hit, distance, -1, QueryTriggerInteraction.Ignore)) {
            detection = Math.Min(detection, hit.distance);
        }
        return detection;
    }

    private List<ODRay> ODRays(float end_angle, float[] markers, float[] distances) {
        float initial_angle = (float)(Math.Asin(1 / distances[0]) * (180.0 / Math.PI));
        float position = initial_angle / end_angle;
        List<ODRay> odr_lst = new List<ODRay>();
        for (int i = 0; i < 50; ++i) {
            float angle = (float)(position * end_angle);
            float distance = distances[distances.Length - 1];
            for (int j = 0; j < markers.Length; ++j) {
                if (angle < markers[j]) {
                    float pos = (angle - markers[j - 1]) / (markers[j] - markers[j - 1]);
                    distance = (float)((1 - pos) * distances[j - 1] + pos * distances[j]);
                    break;
                }
            }
            ODRay odr;
            odr.bearing = angle;
            odr.distance = CastRay(angle, distance);
            odr_lst.Add(odr);
            ODRay odr2;
            odr2.bearing = -angle;
            odr2.distance = CastRay(-angle, distance);
            odr_lst.Add(odr2);

            if (position > 1) {
                return odr_lst;
            }

            float angle_increase = (float)(Math.Asin(2 / distance) * (180.0 / Math.PI));
            position += angle_increase / end_angle;
        }
        Debug.Log("Warning: Too many iterations in ODRays, shortcutting");
        return odr_lst;
    }

    private float CombineODRays(float start_angle, float end_angle, List<ODRay> odrays) {
        float detection = NO_DETECTION;
        foreach (ODRay odray in odrays) {
            if (odray.bearing >= start_angle && odray.bearing <= end_angle) {
                detection = Math.Min(detection, odray.distance);
            }
        }
        return detection;
    }

    private void applyNitro(float throttle) {
        if (throttle > 0 && nitroFuel > nitroCost * Time.deltaTime) {
            nitroFuel -= throttle * nitroCost * Time.deltaTime;
            rb.AddRelativeForce(Vector3.forward * nitroForce * Time.deltaTime);
        }
        nitroFuel += nitroRegen * Time.deltaTime;
        nitroFuel = Mathf.Clamp(nitroFuel, 0, 1);
    }

    private bool checkStuck() {
        if (rb.velocity.magnitude < stuckTolerance * Time.deltaTime) {
            secondsStuck += Time.deltaTime;
        } else {
            secondsStuck = 0;
        }
        return secondsStuck > stuckTimeout;
    }

    private void respawn() {
        secondsStuck = 0;
        Vector3 spawnPosition = tc.GetLastMarker();
        Quaternion spawnRotation = Quaternion.LookRotation(tc.GetNextMarker() - spawnPosition, Vector3.up);
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
    }

    public void FloatMsg(String text) {
        floatText.text = text;
        floatFadeAmount = 0;
    }

    private void FixedUpdate() {
        frames++;
        if (gm.inter.HasCommands()) {
            waitingForCommands = false;
            cmds = gm.inter.GetCommands();
            scriptText.text = cmds.message;
        }

        m_Car.Move(cmds.steering, cmds.acceleration, cmds.brake);
        applyNitro(cmds.nitro);

        if (checkStuck()) {
            Debug.Log("Stuck!");
            FloatMsg("Stuck!\nReset to last waypoint");
            respawn();
        }

        fuelUsed += Mathf.Abs(cmds.acceleration);
        top_speed = Math.Max(top_speed, rb.velocity.magnitude);
        floatText.color = new Color(1f, 1f, 1f, (float)(1 - (Math.Pow(floatFadeFactor, floatFadeAmount) - 1) / (floatFadeFactor - 1)));
        floatFadeAmount = Math.Min(1, floatFadeAmount + 1 / floatFadeTime * Time.fixedDeltaTime);

        if (!waitingForCommands && frames >= lastUpdate + updateEvery) {
            lastUpdate = frames;

            PlayerData data;
            data.speed = rb.velocity.magnitude;

            Vector3[] waypoints = tc.GetNextMarkers(2);
            Vector3 waypoint = waypoints[0] - rb.position;
            data.waypoint_distance = waypoint.magnitude;
            data.waypoint_bearing = Vector3.SignedAngle(waypoint, transform.forward, Vector3.up);
            waypoint = waypoints[1] - rb.position;
            data.future_waypoint_distance = waypoint.magnitude;
            data.future_waypoint_bearing = Vector3.SignedAngle(waypoint, transform.forward, Vector3.up);

            List<ODRay> odrays = ODRays(45, new float[]{0, 2, 6, 10, 22, 45}, new float[]{150, 100, 90, 40, 40, 30});
            data.obstacle_detection_rays = odrays.ToArray();

            // TODO: Remove this from the API, replace with a client-side function
            // Requires updated OD UI
            data.obstacle_detection_center = CombineODRays(-2, 2, odrays);
            data.obstacle_detection_left = CombineODRays(-10, -2, odrays);
            data.obstacle_detection_right = CombineODRays(2, 10, odrays);
            data.obstacle_detection_far_left = CombineODRays(-45, -10, odrays);
            data.obstacle_detection_far_right = CombineODRays(10, 45, odrays);

            data.time = frames * Time.fixedDeltaTime;
            data.frames = frames;

            ShowData(data);
            gm.inter.NewData(data);
            waitingForCommands = true;
        }
    }

    public void EndLevel() {
        EndLevelData data;
        data.time = frames * Time.fixedDeltaTime;
        data.frames = frames;
        data.top_speed = top_speed;
        data.track = SceneManager.GetActiveScene().name;
        data.fuel_used = fuelUsed;
        gm.inter.EndLevel(data);
        if (endLevelText.text == "") {
            endLevelText.text = "Time: " + data.time + " s\nMax speed: " + top_speed + " m/s";
        }
    }

    void ShowData(PlayerData data) {
        dataText.text = "Nitro " + Convert.ToInt32(nitroFuel * 100) + "% ";
        timeText.text = Math.Floor(data.time / 60) + ":" + (data.time % 60 < 10 ? "0" : "") + Math.Floor(data.time % 60);
        SpeedConverter.ShowSpeed(data.speed);
        FuelConverter.ShowFuel(1 - fuelUsed / 10000);
        SensorControl.ShowSensors(data.obstacle_detection_rays);
    }
}
