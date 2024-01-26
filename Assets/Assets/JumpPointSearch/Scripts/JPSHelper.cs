using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace EntitiesTutorials.JumpPointSearch.Scripts
{
    public enum ForceNeighborType
    {
        Horizontal,
        Vertical,
        Slant
    }

    public struct JPSNode
    {
        public int index;
        public int parentIndex;
        public bool canReach;
    }

    public struct OpenListData : IComparable<OpenListData>, IEquatable<OpenListData>
    {
        public int index;
        public int2 dir;
        public int parentIndex;
        public int fCost;
        public int gCost;
        public int hCost;

        public int CompareTo(OpenListData other)
        {
            return fCost.CompareTo(other.fCost);
        }

        public bool Equals(OpenListData other)
        {
            if (other.index == index)
            {
                return other.dir.x == dir.x && other.dir.y == dir.y;
            }

            return false;
        }
    }

    public class JPSHelper
    {
        public readonly static int Definerow = 31; //测试为11x11

        public static void CreateJSPArray(int2 pos, out NativeList<JPSNode> nativeList)
        {
            nativeList = new NativeList<JPSNode>(Definerow * Definerow, Allocator.Persistent);
            for (int i = 0; i < Definerow; i++)
            {
                for (int j = 0; j < Definerow; j++)
                {
                    nativeList.AddNoResize(new JPSNode()
                    {
                        index = i * Definerow + j,
                        parentIndex = -1,
                        canReach = true
                    });
                }
            }
        }

        //根据坐标获取下标
        public static int GetNodeIndexByPos(int2 pos, int2 originPos, ref NativeList<JPSNode> nativeList)
        {
            int mid = nativeList.Length / 2;
            int2 offsetPos = pos - originPos;
            int index = mid + (-offsetPos.y * Definerow + offsetPos.x);
            return index;
        }
        public static int2 GetPosByNodeIndex(int index, int2 originPos, ref NativeList<JPSNode> nativeList)
        {
            int mid = nativeList.Length / 2;
            int2 pos = new int2(index%Definerow-Definerow/2, Definerow/2-index/Definerow);
            return pos;
        }

        public static void SetJSPNodeParentIndex(int nodeIndex, int parentIndex, ref NativeList<JPSNode> nativeList)
        {
            unsafe
            {
                JPSNode* ptr = (JPSNode*)nativeList.GetUnsafePtr();
                ptr[nodeIndex].parentIndex = parentIndex;
            }
        }

        public static void SetJSPNodeCanReach(int nodeIndex, ref NativeList<JPSNode> nativeList, bool canReach = true)
        {
            unsafe
            {
                JPSNode* ptr = (JPSNode*)nativeList.GetUnsafePtr();
                ptr[nodeIndex].canReach = canReach;
            }
        }

        public static void SetJSPNodeCanReach(int2 pos, int2 originPos, ref NativeList<JPSNode> nativeList,
            bool canReach = true)
        {
            var nodeIndex = GetNodeIndexByPos(pos, originPos, ref nativeList);
            unsafe
            {
                JPSNode* ptr = (JPSNode*)nativeList.GetUnsafePtr();
                ptr[nodeIndex].canReach = canReach;
            }
        }

        public static int GetCost(int curIndex, int parentIndex)
        {
            int cost = (math.abs(curIndex / Definerow - parentIndex / Definerow)
                        + math.abs(curIndex % Definerow - parentIndex % Definerow));
            return cost;
        }

        public static NativeList<OpenListData> JSPPathFinding(ref NativeList<JPSNode> nativeList, int curIndex,
            int targetIndex)
        {
            NativeList<OpenListData> openList = new NativeList<OpenListData>(Definerow*3, Allocator.Persistent);
            int gCost = GetCost(curIndex, curIndex);
            int hCost = GetCost(curIndex, targetIndex);
            for (int i = -1; i <= 1; i += 2)
            {
                for (int j = -1; j <= 1; j += 2)
                {
                    openList.AddNoResize(new OpenListData()
                    {
                        index = curIndex,
                        dir = new int2(i, j),
                        gCost = gCost,
                        parentIndex = -1,
                        hCost = hCost,
                        fCost = gCost + hCost
                    });
                }
            }

            var closeList = JSPPathFinding(targetIndex, ref nativeList, ref openList);
            return closeList;
        }

        public static NativeList<int2> ForceNeighbor(ref NativeList<JPSNode> nativeList, int curIndex, int2 dir,
            ForceNeighborType type)
        {
            NativeList<int2> result = new NativeList<int2>(2, Allocator.Persistent);
            int forceNeighBorIndex;
            switch (type)
            {
                case ForceNeighborType.Horizontal:
                    forceNeighBorIndex = ForceNeighborHorizontal(ref nativeList, curIndex, dir.x);
                    if ((forceNeighBorIndex & 1) == 1)
                    {
                        result.AddNoResize(new int2(dir.x, 1));
                    }

                    if ((forceNeighBorIndex & 2) == 2)
                    {
                        result.AddNoResize(new int2(dir.x, -1));
                    }

                    break;
                case ForceNeighborType.Vertical:
                    forceNeighBorIndex = ForceNeighborVertical(ref nativeList, curIndex, dir.y);
                    if ((forceNeighBorIndex & 1) == 1)
                    {
                        result.AddNoResize(new int2(-1, dir.y));
                    }

                    if ((forceNeighBorIndex & 2) == 2)
                    {
                        result.AddNoResize(new int2(1, dir.y));
                    }

                    break;
                case ForceNeighborType.Slant:
                    forceNeighBorIndex = ForceNeighborSlant(ref nativeList, curIndex, dir);
                    if(forceNeighBorIndex!=0)
                        result.AddNoResize(dir);
                    if ((forceNeighBorIndex & 1) == 1)
                    {
                        result.AddNoResize(new int2(-dir.x, dir.y));
                    }

                    if ((forceNeighBorIndex & 2) == 2)
                    {
                        result.AddNoResize(new int2(dir.x, -dir.y));
                    }

                    break;
            }

            return result;
        }

        public static int AddNodeToOpendList(int i, int curNodeIndex, int targetIndex,
            ref OpenListData curNode, ref NativeList<JPSNode> nativeList,
            ref NativeList<OpenListData> openList, ref NativeList<OpenListData> closeList,
            ForceNeighborType type, int parentIndex)
        {
            int result = 0;
            int index;
            var dirList = ForceNeighbor(ref nativeList, i, curNode.dir, type);
            if (dirList.Length <= 0)
                return result;
            int gCost;
            int hCost;
            //把节点i加入到openlist,继续斜线搜索跳点
            if (curNode.index == curNodeIndex)
            {
                //Debug.Log(curNodeIndex+" --"+i);
                index = i;
                gCost = GetCost(i, curNode.index) + curNode.gCost;
                hCost = GetCost(i, targetIndex);
                result = 1;
            }
            //斜向找到跳点，curNodeIndex加入到openlist，退出当前斜向搜索
            else
            {
                index = curNodeIndex;
                gCost = GetCost(curNodeIndex, curNode.index) + curNode.gCost;
                hCost = GetCost(curNodeIndex, targetIndex);
                result = 2;
            }

            foreach (var dir in dirList)
            {
                //Debug.Log(index+"  "+dir);
                //Debug.Log($"index:{index}  fCost:{gCost + hCost}");
                OpenListData data = new OpenListData()
                {
                    index = index,
                    dir = dir,
                    parentIndex = parentIndex,
                    gCost = gCost,
                    hCost = hCost,
                    fCost = gCost + hCost
                };
                if (openList.Contains(data) || closeList.Contains(data))
                {
                    continue;
                }
                openList.AddNoResize(data);
            }

            dirList.Dispose();
            return result;
        }

        public static NativeList<OpenListData> FindTarget(ref NativeList<OpenListData> closeList, int i,
            int curNodeIndex,
            OpenListData curNode)
        {
            int parentIndex = closeList.Length - 1;
            if (curNode.index != curNodeIndex)
            {
                closeList.AddNoResize(new OpenListData()
                {
                    index = curNodeIndex,
                    dir = curNode.dir,
                    parentIndex = parentIndex,
                });
            }

            parentIndex = closeList.Length - 1;
            closeList.AddNoResize(new OpenListData()
            {
                index = i,
                dir = curNode.dir,
                parentIndex = parentIndex,
            });
            return closeList;
        }

        public static NativeList<OpenListData> JSPPathFinding(int targetIndex, ref NativeList<JPSNode> nativeList,
            ref NativeList<OpenListData> openList)
        {
            NativeList<OpenListData> closeList = new NativeList<OpenListData>(Definerow * Definerow, Allocator.Persistent);
            bool findJumpPointSlant;
            if (openList.Length <= 0)
                return closeList;
            do
            {
                int value = int.MaxValue;
                int index = int.MaxValue;
                for (int i = 0; i < openList.Length; i++)
                {
                    if (openList[i].fCost < value)
                    {
                        value = openList[i].fCost;
                        index = i;
                    }
                }

                var curNode = openList[index];
                int curNodeIndex = curNode.index;
                openList.RemoveAt(index);
                closeList.AddNoResize(curNode);
                var parentIndex = closeList.Length - 1;
                findJumpPointSlant = false;
                for (;
                     curNodeIndex >= 0 && curNodeIndex < Definerow * Definerow;)
                {
                    //Debug.Log(curNodeIndex + "  " + curNode.index + "  " + curNode.dir);
                    //斜向找到目标点
                    if (curNodeIndex == targetIndex)
                    {
                        Debug.Log("findTarget:" + targetIndex);
                        return closeList;
                    }

                    int forceNeighBorIndex = -1;
                    //水平遍历
                    for (int i = curNodeIndex;
                         i < (curNodeIndex / Definerow * Definerow + Definerow) &&
                         i >= curNodeIndex / Definerow * Definerow;
                         i += curNode.dir.x)
                    {
                        if (i == targetIndex)
                        {
                            Debug.Log("findTargetHorizontal:" + targetIndex);
                            return FindTarget(ref closeList, i, curNodeIndex, curNode);
                        }

                        if (!nativeList[i].canReach)
                            break;
                        forceNeighBorIndex = AddNodeToOpendList(i, curNodeIndex, targetIndex, ref curNode,
                            ref nativeList, ref openList, ref closeList,
                            ForceNeighborType.Horizontal, parentIndex);
                        if(forceNeighBorIndex==2)
                            findJumpPointSlant = true;
                    }

                    //垂直遍历
                    for (int i = curNodeIndex;
                         i < (Definerow * Definerow - curNodeIndex % Definerow) && i >= curNodeIndex % Definerow;
                         i -= curNode.dir.y * Definerow)
                    {
                        if (i == targetIndex)
                        {
                            //Debug.Log("findTargetVertical:" + targetIndex);
                            return FindTarget(ref closeList, i, curNodeIndex, curNode);
                        }

                        if (!nativeList[i].canReach)
                            break;
                        forceNeighBorIndex = AddNodeToOpendList(i, curNodeIndex, targetIndex, ref curNode,
                            ref nativeList, ref openList, ref closeList,
                            ForceNeighborType.Vertical, parentIndex);
                        if(forceNeighBorIndex==2)
                            findJumpPointSlant = true;
                    }

                    //找到跳点
                    if (findJumpPointSlant)
                        break;
                    //没找到跳点，沿着反向前进一步
                    int nextIndex = curNodeIndex - Definerow * curNode.dir.y + curNode.dir.x;
                    if (nextIndex == targetIndex)
                    {
                        //Debug.Log("findTargetSlant" + nextIndex);
                        return FindTarget(ref closeList, nextIndex, nextIndex, curNode);
                    }
                    if(nextIndex>Definerow*Definerow-1||nextIndex<0)
                        break;
                    if (curNodeIndex % Definerow + nextIndex % Definerow == Definerow - 1 ||
                        curNodeIndex / Definerow + nextIndex / Definerow == Definerow - 1 || nextIndex < 0)
                        break;
                    curNodeIndex = nextIndex;
                    if (!nativeList[curNodeIndex].canReach)
                        break;

                    forceNeighBorIndex = AddNodeToOpendList(curNodeIndex, curNodeIndex, targetIndex, ref curNode,
                        ref nativeList, ref openList, ref closeList,
                        ForceNeighborType.Slant, parentIndex);
                    if (forceNeighBorIndex == 2)
                    {
                        break;
                    }
                }
            } while (openList.Length > 0);

            return closeList;
        }

        public static int ForceNeighborHorizontal(ref NativeList<JPSNode> nativeList, int curIndex, int dir)
        {
            int result = 0;
            int upNeighborIndex = curIndex - Definerow;
            int downNeighborIndex = curIndex + Definerow;
            if (curIndex % Definerow == 0 || curIndex % Definerow == Definerow - 1)
                return 0;

            if (upNeighborIndex >= 0 && upNeighborIndex + dir >= 0)
            {
                if (!nativeList[upNeighborIndex].canReach && nativeList[upNeighborIndex + dir].canReach)
                {
                    result += 1;
                }
            }

            if (downNeighborIndex < Definerow * Definerow && downNeighborIndex + dir < Definerow * Definerow)
            {
                if (!nativeList[downNeighborIndex].canReach && nativeList[downNeighborIndex + dir].canReach)
                {
                    result += 2;
                }
            }

            return result;
        }

        public static int ForceNeighborVertical(ref NativeList<JPSNode> nativeList, int curIndex, int dir)
        {
            int result = 0;
            int leftNeighborIndex = curIndex - 1;
            int rightNeighborIndex = curIndex + 1;

            if (!(curIndex % Definerow == 0) && leftNeighborIndex - dir * Definerow >= 0 &&
                leftNeighborIndex - dir * Definerow < Definerow * Definerow)
            {
                if (!nativeList[leftNeighborIndex].canReach && nativeList[leftNeighborIndex - dir * Definerow].canReach)
                {
                    result += 1;
                }
            }

            if (!(curIndex % Definerow == Definerow - 1) && rightNeighborIndex - dir * Definerow < Definerow * Definerow &&
                rightNeighborIndex - dir * Definerow >= 0)
            {
                if (!nativeList[rightNeighborIndex].canReach &&
                    nativeList[rightNeighborIndex - dir * Definerow].canReach)
                {
                    result += 2;
                }
            }

            return result;
        }

        public static int ForceNeighborSlant(ref NativeList<JPSNode> nativeList, int curIndex, int2 dir)
        {
            int result = 0;
            if (curIndex - dir.x >= 0 && (curIndex - dir.x) - dir.y * Definerow >= 0)
            {
                if (!nativeList[curIndex - dir.x].canReach &&
                    nativeList[(curIndex - dir.x) - dir.y * Definerow].canReach)
                {
                    result += 1;
                }
            }

            if (curIndex + dir.y * Definerow < Definerow &&
                curIndex + dir.y * Definerow + dir.x <= Definerow * Definerow)
            {
                if (!nativeList[curIndex + dir.y * Definerow].canReach &&
                    nativeList[curIndex + dir.y * Definerow + dir.x].canReach)
                {
                    result += 2;
                }
            }
            return result;
        }
    }
}