using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MovingTexture : MonoBehaviour {
	public float  scollX = 0.5f;
	public float scollY = 0.5f;

	// Update is called once per frame
	void Update () {
		float offsetX = Time.time *scollX;
		float offsetY = Time.time *scollY;
		GetComponent<Renderer>().material.mainTextureOffset = new Vector2 (offsetX, offsetY);
	}
}
