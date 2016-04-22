#region license
/*The MIT License (MIT)
ModuleEVAStrut - Part module to control the EVA strut

Copyright (c) 2016 DMagic

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CompoundParts;
using Experience;
using UnityEngine;

namespace EVAStruts
{
    public class ModuleEVAStrut : CModuleLinkedMesh
    {
		[KSPField]
		public string useProfession = "Engineer";
		[KSPField]
		public int minLevel = 0;
		[KSPField]
		public float maxDistance = 20;

		private Vessel EVA;
		private Transform EVATransform;
		private Transform EVAJetpack;

		private CModuleStrut linkedStrutModule;

		private CompoundPart.AttachState EVAAttachState;

		private float connectionDistance;

		private Transform compoundTransform;

		private RaycastHit hit;

		private uint targetID;
		private int waitTimer;
		private bool loaded;

		public override void OnStart(PartModule.StartState state)
		{
			useProfession = professionValid(useProfession);
			minLevel = (int)clampValue(minLevel, 0, 5);
			maxDistance = clampValue(maxDistance, 10, 500);

			compoundTransform = compoundPart.transform;
			compoundPart.maxLength = maxDistance;

			linkedStrutModule = part.FindModulesImplementing<CModuleStrut>().FirstOrDefault();

			if (linkedStrutModule == null)
			{
				Debug.LogWarning("[EVA Strut] Error in detecting the linked strut part module; removing this object...");
				Destroy(gameObject);
			}

			base.OnStart(state);

			if (state == StartState.Editor)
				return;

			Events["pickupEVAStrut"].guiName = "Pickup EVA Strut";
			Events["cutEVAStrut"].guiName = "Cut EVA Strut";
			Events["dropEVAStrut"].guiName = "Drop EVA Strut";
		}

		private void Update()
		{
			if (HighLogic.LoadedSceneIsEditor)
			{
				if (compoundPart.attachState == CompoundPart.AttachState.Attaching)
				{

					compoundPart.attachState = CompoundPart.AttachState.Detached;
					InputLockManager.ClearControlLocks();
				}
				return;
			}

			if (EVAAttachState != CompoundPart.AttachState.Attaching)
				return;

			if (waitTimer < 5)
			{
				waitTimer++;
				return;
			}

			if (!HighLogic.LoadedSceneIsFlight)
				return;

			if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, 20, 1) && checkDistance)
			{
				//print("[EVA Strut] Hit Target");
				Part p = hit.collider.gameObject.GetComponentUpwards<Part>();
				Vector3 dir = compoundTransform.InverseTransformPoint(hit.point).normalized;

				if (p == null || p.vessel != this.vessel || p.vessel.isEVA)
				{
					//print("[EVA Strut] Wrong Target Type...");
					compoundPart.direction = Vector3.zero;
					setKerbalAttach();
				}
				else
				{
					int layer = gameObject.layer;
					gameObject.SetLayerRecursive(2, 0);

					if (Physics.Raycast(compoundTransform.position, compoundTransform.TransformDirection(dir), out hit, maxDistance, 1 << 0) && checkDistance)
					{
						p = hit.collider.gameObject.GetComponentUpwards<Part>();

						if (p == null || p.vessel != this.vessel || p.vessel.isEVA)
						{
							//print("[EVA Strut] Wrong Target Type...");
							compoundPart.direction = Vector3.zero;
							setKerbalAttach();
						}
						else
						{
							//print("[EVA Strut] Found Target....");
							compoundPart.target = p;

							compoundPart.direction = compoundTransform.InverseTransformPoint(hit.point).normalized;
							compoundPart.targetPosition = compoundTransform.InverseTransformPoint(hit.point);
							compoundPart.targetRotation = Quaternion.FromToRotation(Vector3.right, compoundTransform.InverseTransformDirection(hit.normal));

							if (Input.GetMouseButtonUp(0))
							{
								if ((compoundTransform.position - hit.point).magnitude < maxDistance)
								{
									gameObject.SetLayerRecursive(layer, 0);
									attachStrut();
								}
							}
						}
					}
					else
					{
						compoundPart.direction = Vector3.zero;
						setKerbalAttach();
					}

					if (EVAAttachState == CompoundPart.AttachState.Attached)
					{
						return;
					}

					gameObject.SetLayerRecursive(layer, 0);
				}
			}
			else
				setKerbalAttach();

			base.OnPreviewAttachment(compoundPart.direction, compoundPart.targetPosition, compoundPart.targetRotation);
		}

		public override void OnSave(ConfigNode node)
		{
			if (EVAAttachState == CompoundPart.AttachState.Attached)
			{
				node.AddValue("TargetPartID", targetID);
			}

			base.OnSave(node);
		}

		public override void OnLoad(ConfigNode node)
		{
			base.OnLoad(node);

			if (!HighLogic.LoadedSceneIsFlight)
				return;

			if (!node.HasValue("TargetPartID"))
			{
				severStrut();
				return;
			}

			try
			{
				targetID = uint.Parse(node.GetValue("TargetPartID"));
			}
			catch (Exception e)
			{
				print("[EVA Refuel] Exception in assigning target part ID:\n" + e);
			}

			EVAAttachState = CompoundPart.AttachState.Attached;

			StartCoroutine(loadConnections());
		}

		IEnumerator loadConnections()
		{
			if (!HighLogic.LoadedSceneIsFlight)
				yield break;

			while (!FlightGlobals.ready && !compoundPart.started)
			{
				yield return null;
			}

			int timer = 0;

			while (timer < 30)
			{
				timer++;
				yield return null;
			}

			try
			{
				compoundPart.target = vessel.Parts.FirstOrDefault(p => p.craftID == targetID);
			}
			catch (Exception e)
			{
				print("[EVA Strut] Exception While Loading Target Part\n" + e);
				severStrut();
				loaded = true;
				yield break;
			}

			if (compoundPart.target == null)
			{
				severStrut();
				print("[EVA Strut] Target Part Not Found...");
				loaded = true;
				yield break;
			}

			attachStrut();
			loaded = true;
		}

		private void OnDestroy()
		{
			if (EVAAttachState != CompoundPart.AttachState.Attached)
				severStrut();
		}

		public override void OnTargetSet(Part target)
		{
			if (linkedStrutModule != null)
			{
				linkedStrutModule.OnTargetSet(target);
			}

			base.OnTargetSet(target);
		}

		public override void OnTargetLost()
		{
			if (linkedStrutModule != null)
				linkedStrutModule.OnTargetLost();

			base.OnTargetLost();
		}

		private void LateUpdate()
		{
			if (EVAAttachState != CompoundPart.AttachState.Attached)
				return;

			OnTargetUpdate();
		}

		public override void OnTargetUpdate()
		{
			base.OnTargetUpdate();
		}

		private void FixedUpdate()
		{
			if (EVAAttachState != CompoundPart.AttachState.Attached)
				return;

			if (!loaded)
				return;

			if (target == null || target.vessel != vessel)
				severStrut();
		}

		public override void OnPreviewAttachment(UnityEngine.Vector3 rDir, UnityEngine.Vector3 rPos, UnityEngine.Quaternion rRot)
		{
			base.OnPreviewAttachment(rDir, rPos, rRot);
		}

		[KSPEvent(guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = true, active = true, unfocusedRange = 4)]
		public void pickupEVAStrut()
		{
			if (!checkEVAVessel)
			{
				ScreenMessages.PostScreenMessage("Current Vessel is not an EVA Kerbal...", 6f, ScreenMessageStyle.UPPER_CENTER);
				return;
			}

			if (!checkProfession)
			{
				ScreenMessages.PostScreenMessage("The Kerbal must be an " + useProfession + " to attach the EVA strut.", 6f, ScreenMessageStyle.UPPER_CENTER);
				return;
			}

			if (!checkLevel)
			{
				ScreenMessages.PostScreenMessage("The Kerbal must be above level " + minLevel + " to attach the EVA strut.", 6f, ScreenMessageStyle.UPPER_CENTER);
				return;
			}

			if (!setEVAPosition())
				return;

			Events["pickupEVAStrut"].active = false;
			Events["dropEVAStrut"].active = true;

			EVAAttachState = CompoundPart.AttachState.Attaching;
			compoundPart.attachState = CompoundPart.AttachState.Detached;
			waitTimer = 0;
		}

		[KSPEvent(guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = true, active = false, unfocusedRange = 4)]
		public void cutEVAStrut()
		{
			if (FlightGlobals.ActiveVessel.isEVA)
			{
				if (!checkEVAVessel)
					return;

				if (!checkEVADistance)
					return;
			}

			severStrut();
		}

		[KSPEvent(guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = true, active = false, unfocusedRange = 10)]
		public void dropEVAStrut()
		{
			Events["dropEVAStrut"].active = false;
			Events["pickupEVAStrut"].active = true;
			Events["cutEVAStrut"].active = false;
			Events["cutEVAStrut"].unfocusedRange = 4;

			EVAAttachState = CompoundPart.AttachState.Detached;
			compoundPart.attachState = CompoundPart.AttachState.Detached;

			OnTargetLost();
		}

		private void attachStrut()
		{
			targetID = compoundPart.target.craftID;

			Events["dropEVAStrut"].active = false;
			Events["pickupEVAStrut"].active = false;
			Events["cutEVAStrut"].active = true;

			OnTargetSet(compoundPart.target);

			connectionDistance = (targetAnchor.position - compoundTransform.position).magnitude;

			Events["cutEVAStrut"].unfocusedRange = connectionDistance + 10;

			compoundPart.attachState = CompoundPart.AttachState.Detached;
			EVAAttachState = CompoundPart.AttachState.Attached;
		}

		private void severStrut()
		{
			Events["dropEVAStrut"].active = false;
			Events["pickupEVAStrut"].active = true;
			Events["cutEVAStrut"].active = false;
			Events["cutEVAStrut"].unfocusedRange = 4;

			OnTargetLost();

			compoundPart.target = null;

			compoundPart.direction = Vector3.zero;
			compoundPart.targetPosition = Vector3.zero;
			compoundPart.targetRotation = Quaternion.identity;

			compoundPart.attachState = CompoundPart.AttachState.Detached;
			EVAAttachState = CompoundPart.AttachState.Detached;

			//targetAnchor.gameObject.SetActive(false);
		}

		private string professionValid(string s)
		{
			switch (s)
			{
				case "Pilot":
				case "Engineer":
				case "Scientist":
					return s;
				case "pilot":
					return "Pilot";
				case "engineer":
					return "Engineer";
				case "scientist":
					return "Scientist";
			}

			return "";
		}

		private float clampValue(float value, float min, float max)
		{
			return Mathf.Clamp(value, min, max);
		}

		private void setKerbalAttach()
		{
			if (EVAJetpack == null)
				compoundPart.attachState = CompoundPart.AttachState.Detached;

			compoundPart.direction = base.transform.InverseTransformPoint(EVAJetpack.position).normalized;
			compoundPart.targetPosition = base.transform.InverseTransformPoint(EVAJetpack.position);
			compoundPart.targetRotation = Quaternion.FromToRotation(Vector3.left, base.transform.InverseTransformDirection(EVAJetpack.position));
		}

		private bool setEVAPosition()
		{
			List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>(EVA.rootPart.GetComponentsInChildren<SkinnedMeshRenderer>() as SkinnedMeshRenderer[]);
			foreach (SkinnedMeshRenderer m in meshes)
			{
				if (m == null)
					continue;

				if (m.name != "jetpack_base01")
					continue;

				foreach (Transform bone in m.bones)
				{
					if (bone == null)
						continue;

					if (bone.name != "bn_jetpack01")
						continue;

					EVAJetpack = bone.transform;
					return true;
				}
			}

			return false;
		}

		private bool checkEVAVessel
		{
			get
			{
				EVA = FlightGlobals.ActiveVessel;

				if (EVA == null)
					return false;

				EVATransform = EVA.transform;

				if (!EVA.isEVA)
					return false;

				if (EVA.GetVesselCrew().Count != 1)
					return false;

				return true;
			}
		}

		private bool checkProfession
		{
			get
			{
				if (string.IsNullOrEmpty(useProfession))
					return true;

				if (EVA.GetVesselCrew().First().experienceTrait.TypeName != useProfession)
					return false;

				return true;
			}
		}

		private bool checkLevel
		{
			get
			{
				if (minLevel <= 0)
					return true;

				if (EVA.GetVesselCrew().First().experienceLevel < minLevel)
					return false;

				return true;
			}
		}

		private bool checkDistance
		{
			get
			{
				return EVA == FlightGlobals.ActiveVessel && (EVATransform.position - hit.point).magnitude < 8 && (compoundTransform.position - hit.point).magnitude < maxDistance;
			}
		}

		private bool checkEVADistance
		{
			get
			{
				if (FlightGlobals.ActiveVessel.isEVA)
				{
					return (compoundTransform.position - EVATransform.position).magnitude < 10 || (targetAnchor.position - EVATransform.position).magnitude < 10;
				}

				return true;
			}
		}
    }
}
