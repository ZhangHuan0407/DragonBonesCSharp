using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DragonBones;

namespace DragonBones
{
    [DisallowMultipleComponent]
    [ExecuteInEditMode]
    [RequireComponent(typeof(UnityArmatureComponent))]
    public class UnityCombineMeshs : MonoBehaviour
    {
        [HideInInspector]
        public List<string> slotNames = new List<string>();
        [HideInInspector]
        public MeshBuffer[] meshBuffers;
        [HideInInspector]
        public bool dirty = false;

        private UnityArmatureComponent _unityArmature;
        private int _subSlotCount;
        private int _verticeOffset;

        private bool _isCanCombineMesh = false;

        private void Start()
        {
            this._unityArmature = GetComponent<UnityArmatureComponent>();
            this._isCanCombineMesh = true;
            this.dirty = true;
        }

        private void OnDestroy()
        {
            if (this._unityArmature != null)
            {
                this.RestoreArmature(this._unityArmature._armature);
            }

            if (this.meshBuffers != null)
            {
                for (var i = 0; i < this.meshBuffers.Length; i++)
                {
                    var meshBuffer = this.meshBuffers[i];
                    meshBuffer.Dispose();
                }
            }

            this.meshBuffers = null;
            this.dirty = false;

            this._unityArmature = null;
            this._subSlotCount = 0;
            this._verticeOffset = -1;

            this._isCanCombineMesh = false;
        }

        private void RestoreArmature(Armature armature)
        {
            if (armature == null)
            {
                return;
            }
            //
            foreach (UnitySlot slot in armature.GetSlots())
            {
                if (slot.childArmature == null)
                {
                    slot.CancelCombineMesh();
                }
            }
        }

        private void LateUpdate()
        {
            if (this.dirty)
            {
                this.BeginCombineMesh();
                this.dirty = false;
            }

            if (this.meshBuffers == null)
            {
                return;
            }

            for (var i = 0; i < this.meshBuffers.Length; i++)
            {
                var meshBuffer = this.meshBuffers[i];
                if (meshBuffer.zorderDirty)
                {
                    meshBuffer.UpdateOrder();
                    meshBuffer.zorderDirty = false;
                }
                else if (meshBuffer.vertexDirty)
                {
                    meshBuffer.UpdateVertices();
                    meshBuffer.vertexDirty = false;
                }
            }
        }

        public void BeginCombineMesh()
        {
            if (!this._isCanCombineMesh || _unityArmature.isUGUI)
            {
                return;
            }
            //
            this._verticeOffset = 0;
            this._subSlotCount = 0;
            this.slotNames.Clear();

            //
            if (this.meshBuffers != null)
            {
                for (var i = 0; i < this.meshBuffers.Length; i++)
                {
                    var meshBuffer = this.meshBuffers[i];
                    meshBuffer.Dispose();
                }

                this.meshBuffers = null;
            }

            List<CombineMeshInfo> combineSlots = new List<CombineMeshInfo>();
            //
            this.CollectMesh(this._unityArmature.armature, combineSlots);

            //
            //先合并
            List<MeshBuffer> buffers = new List<MeshBuffer>();
            for (var i = 0; i < combineSlots.Count; i++)
            {
                var combineSlot = combineSlots[i];

                //
                var proxySlot = combineSlot.proxySlot;
                MeshBuffer meshBuffer = new MeshBuffer();
                meshBuffer.name = proxySlot._meshBuffer.name;
                meshBuffer.sharedMesh = MeshBuffer.GenerateMesh();
                meshBuffer.sharedMesh.Clear();

                meshBuffer.CombineMeshes(combineSlot.combines.ToArray());
                meshBuffer.vertexDirty = true;
                //
                proxySlot._meshFilter.sharedMesh = meshBuffer.sharedMesh;

                buffers.Add(meshBuffer);

                //
                this._verticeOffset = 0;
                for (int j = 0; j < combineSlot.slots.Count; j++)
                {
                    var slot = combineSlot.slots[j];

                    slot._isCombineMesh = true;
                    slot._sumMeshIndex = i;
                    slot._verticeOrder = j;
                    slot._verticeOffset = this._verticeOffset;
                    slot._combineMesh = this;
                    slot._meshBuffer.enabled = false;
                    if (slot._renderDisplay != null)
                    {
                        slot._renderDisplay.SetActive(false);
                        slot._renderDisplay.hideFlags = HideFlags.HideInHierarchy;
                    }

                    //
                    meshBuffer.combineSlots.Add(slot);

                    this.slotNames.Add(slot.name);

                    this._verticeOffset += slot._meshBuffer.vertexBuffers.Length;
                    this._subSlotCount++;
                }

                //被合并的显示
                if (proxySlot._renderDisplay != null)
                {
                    proxySlot._renderDisplay.SetActive(true);
                    proxySlot._renderDisplay.hideFlags = HideFlags.None;
                }
            }

            this.meshBuffers = buffers.ToArray();
        }

        public void CollectMesh(Armature armature, List<CombineMeshInfo> combineSlots)
        {
            var slots = new List<Slot>(armature.GetSlots());
            if (slots.Count == 0)
            {
                return;
            }
            //
            var isBreakCombineMesh = false;
            var isSameMaterial = false;
            var isChildAramture = false;
            UnitySlot slotMeshProxy = null;

            GameObject slotDisplay = null;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i] as UnitySlot;

                slot.CancelCombineMesh();

                isChildAramture = slot.childArmature != null;
                slotDisplay = slot.renderDisplay;

                if (slotMeshProxy != null)
                {
                    if (slot._meshBuffer.name == string.Empty)
                    {
                        isSameMaterial = true;
                    }
                    else
                    {
                        isSameMaterial = slotMeshProxy._meshBuffer.name == slot._meshBuffer.name;
                    }
                }
                else
                {
                    isSameMaterial = slotMeshProxy == null;
                }

                //先检查这个slot会不会打断网格合并
                isBreakCombineMesh = isChildAramture ||
                                    slot._isIgnoreCombineMesh ||
                                    !isSameMaterial;

                //如果会打断，那么先合并一次
                if (isBreakCombineMesh)
                {
                    if (combineSlots.Count > 0)
                    {
                        if (combineSlots[combineSlots.Count - 1].combines.Count == 1)
                        {
                            combineSlots.RemoveAt(combineSlots.Count - 1);
                        }
                    }

                    slotMeshProxy = null;
                }
                //
                if (slotMeshProxy == null && !isBreakCombineMesh && slotDisplay != null && slotDisplay.activeSelf)
                {
                    CombineMeshInfo combineSlot = new CombineMeshInfo();
                    combineSlot.proxySlot = slot;
                    combineSlot.combines = new List<CombineInstance>();
                    combineSlot.slots = new List<UnitySlot>();
                    combineSlots.Add(combineSlot);

                    slotMeshProxy = slot;
                }

                //如果不会合并，检查一下是否是子骨架
                if (isChildAramture)
                {
                    continue;
                }

                if (slotMeshProxy != null && slotDisplay != null && slotDisplay.activeSelf && !slot._isIgnoreCombineMesh)
                {
                    var parentTransfrom = (slot._armature.proxy as UnityArmatureComponent).transform;
                    CombineInstance com = new CombineInstance();
                    com.mesh = slot._meshBuffer.sharedMesh;
                    com.transform = slotMeshProxy._renderDisplay.transform.worldToLocalMatrix * slotDisplay.transform.localToWorldMatrix;

                    combineSlots[combineSlots.Count - 1].combines.Add(com);
                    combineSlots[combineSlots.Count - 1].slots.Add(slot);
                }
                if (i != slots.Count - 1)
                {
                    continue;
                }
                //
                if (combineSlots.Count > 0)
                {
                    if (combineSlots[combineSlots.Count - 1].combines.Count == 1)
                    {
                        combineSlots.RemoveAt(combineSlots.Count - 1);
                    }
                }
                slotMeshProxy = null;
            }
        }
    }

    public struct CombineMeshInfo
    {
        public UnitySlot proxySlot;
        public List<CombineInstance> combines;
        public List<UnitySlot> slots;
    }
}