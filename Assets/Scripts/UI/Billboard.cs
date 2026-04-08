using UnityEngine;

public class Billboard : MonoBehaviour
{
	private Camera mainCamera;

	void Start()
	{
		mainCamera = Camera.main;
	}

	void LateUpdate()
	{
		if (mainCamera == null)
		{
			mainCamera = Camera.main;
			if (mainCamera == null) return;
		}
		// Make the object face the camera
		transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
						mainCamera.transform.rotation * Vector3.up);
	}
}
