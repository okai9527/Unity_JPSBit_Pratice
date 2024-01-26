using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;


namespace EntitiesTutorials.JumpPointSearch.Scripts
{
    public  class JPSBitMain : MonoBehaviour
    {
        private static NativeList<JPSBitHelper.OpenListData> path;
        private unsafe static JPSBitHelper.OpenListData* ptr=default;
        static int targetIndex=0;
        private int2 originPos=int2.zero;

        private void Start()
        {
            JPSBitHelper.InitDic();
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Vector3 mousePosition = Input.mousePosition;
                mousePosition.z = JPSBitHelper.Definerow;
                var worldPos = Camera.main.ScreenToWorldPoint(mousePosition);
                originPos =  new int2((int)Math.Round(worldPos.x), (int)Math.Round(worldPos.y));
                JPSBitHelper.CreateJPSArray();
            }

            if (Input.GetMouseButton(1))
            {
                Vector3 mousePosition = Input.mousePosition;
                mousePosition.z = JPSBitHelper.Definerow;
                var worldPos = Camera.main.ScreenToWorldPoint(mousePosition);
                int2 obstaclePos = new int2((int)Math.Round(worldPos.x), (int)Math.Round(worldPos.y));
                unsafe
                {
                    JPSBitHelper.SetJPSNodeCanReach(obstaclePos, originPos, false);
                }
                // obstacleList.AddNoResize(new Vector2(obstaclePos.x,obstaclePos.y));
                // Debug.Log(obstacleList.Length);
            }

            if (Input.GetMouseButtonDown(2))
            {
                Vector3 mousePosition = Input.mousePosition;
                mousePosition.z = JPSBitHelper.Definerow;
                var worldPos = Camera.main.ScreenToWorldPoint(mousePosition);
                int2 targetPos =  new int2((int)Math.Round(worldPos.x), (int)Math.Round(worldPos.y));
                Stopwatch sw = new Stopwatch();
                sw.Start();
                int couter = 0;
                while (couter<100)
                {
                    unsafe
                    {
                        var reslut=JPSBitHelper.JPSPathFinding(originPos,originPos,targetPos,out int resultIndex);
                        targetIndex = resultIndex;
                        //Debug.Log(resultIndex);
                        ptr = reslut;
                    }
                    couter++;
                }
                sw.Stop();
                Debug.Log($"showTime:{sw.ElapsedMilliseconds}");
                
            }

            Vector3 originPosV3 = new Vector3(originPos.x, originPos.y);
            // 绘制垂直线
            for (float x = -JPSBitHelper.Definerow / 2 - 0.5f; x <= JPSBitHelper.Definerow / 2 + 0.5f; x++)
            {
                Vector3 startPos = new Vector3(x, -JPSBitHelper.Definerow / 2 - 0.5f, 0)+originPosV3;
                Vector3 endPos = new Vector3(x, JPSBitHelper.Definerow / 2 + 0.5f, 0)+originPosV3;
                Debug.DrawLine(startPos, endPos,Color.white);
            }

            // 绘制水平线
            for (float y = -JPSBitHelper.Definerow / 2 - 0.5f; y <= JPSBitHelper.Definerow / 2 + 0.5f; y++)
            {
                Vector3 startPos = new Vector3(-JPSBitHelper.Definerow / 2 - 0.5f, y, 0)+originPosV3;
                Vector3 endPos = new Vector3(JPSBitHelper.Definerow / 2 + 0.5f, y, 0)+originPosV3;
                Debug.DrawLine(startPos, endPos,Color.white);
            }

        }

        void OnDrawGizmos()
        {
            Vector3 originPosV3 = new Vector3(originPos.x, originPos.y);
            Gizmos.DrawCube(originPosV3,
                new Vector3(1f, 1f, 0));
            if(JPSBitHelper.nativeListHorizontal.IsEmpty)
                return;
            for (int i = 0; i <JPSBitHelper.nativeListHorizontal.Length; i++)
            {
                var value = JPSBitHelper.nativeListHorizontal[i];
                uint offset;
                while (value>0)
                {
                    Gizmos.color=Color.black;
                    offset = JPSBitHelper.NumberOfLeadingZerosLong(value);
                    Gizmos.DrawCube(new Vector3( offset-JPSBitHelper.Definerow/2,JPSBitHelper.Definerow/2-i)+originPosV3, new Vector3(1f, 1f, 0));
                    value &=(ulong.MaxValue - JPSBitHelper._hashMap[(int) offset]);
                }
            }
        
            unsafe
            {
                if (targetIndex>0)
                {
                    int index = 0;
                    while (index<JPSBitMain.targetIndex)
                    {
                        Gizmos.color=Color.cyan;
                        var pos= JPSBitHelper.GetPosByNodeIndex(ptr[index].index, originPos);
                        Gizmos.DrawCube(new Vector3(pos.x,pos.y), new Vector3(1f, 1f, 0));
                        index++;
                    }
                    var pathNodeIndex = JPSBitMain.targetIndex;
                    Gizmos.color=Color.green;
                    while (pathNodeIndex != -1)
                    {
                        var pos1= JPSBitHelper.GetPosByNodeIndex(ptr[pathNodeIndex].index, originPos);
                        Gizmos.DrawCube(new Vector3(pos1.x,pos1.y), new Vector3(1f, 1f, 0));
                        pathNodeIndex = ptr[pathNodeIndex].parentIndex;
                    }
                }
            }
        }
    }
}