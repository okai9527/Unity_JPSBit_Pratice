using System;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace EntitiesTutorials.JumpPointSearch.Scripts
{
    public enum TraversalDirection
    {
        Horizontal,
        Vertical
    }

    public enum Direction
    {
        Forward,
        Reverse
    }
    
    public struct JPSBitHelper
    {
        private static ProfilerMarker check1;
        private static ProfilerMarker check2;
        private static ProfilerMarker check3;
        private static ProfilerMarker check4;
        public static NativeParallelHashMap<int, ulong> _hashMap;

        /// <summary>
        /// 二进制求前导零
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public static uint NumberOfLeadingZerosLong(ulong x)
        {
            // Do the smearing which turns (for example)
            // this: 0000 0101 0011
            // into: 0000 0111 1111
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            x |= x >> 32;

            // Count the ones
            x -= x >> 1 & 0x5555555555555555;
            x = (x >> 2 & 0x3333333333333333) + (x & 0x3333333333333333);
            x = (x >> 4) + x & 0x0f0f0f0f0f0f0f0f;
            x += x >> 8;
            x += x >> 16;
            x += x >> 32;

            const int numLongBits = sizeof(long) * 8; // compile time constant
            return numLongBits - (uint) (x & 0x0000007f); // subtract # of 1s from 64
        }

        /// <summary>
        /// 二进制求后导零
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public static uint NumberOfTrailingZerosLong(ulong x)
        {
            // Do the smearing which turns (for example)
            // this: 1101 0100 0000
            // into: 1111 1111 1111
            x |= x << 1;
            x |= x << 2;
            x |= x << 4;
            x |= x << 8;
            x |= x << 16;
            x |= x << 32;

            // Count the ones
            x -= x >> 1 & 0x5555555555555555;
            x = (x >> 2 & 0x3333333333333333) + (x & 0x3333333333333333);
            x = (x >> 4) + x & 0x0f0f0f0f0f0f0f0f;
            x += x >> 8;
            x += x >> 16;
            x += x >> 32;

            const int numLongBits = sizeof(long) * 8; // compile time constant
            return numLongBits - (uint) (x & 0x0000007f); // subtract # of 1s from 64
        }

        /// <summary>
        /// 二进制逆序
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public static uint Reverse(uint x)
        {
            x = (((x & 0xaaaaaaaa) >> 1) | ((x & 0x55555555) << 1));
            x = (((x & 0xcccccccc) >> 2) | ((x & 0x33333333) << 2));
            x = (((x & 0xf0f0f0f0) >> 4) | ((x & 0x0f0f0f0f) << 4));
            x = (((x & 0xff00ff00) >> 8) | ((x & 0x00ff00ff) << 8));

            return ((x >> 16) | (x << 16));
        }

        /// <summary>
        /// 加入到openList的数据
        /// </summary>
        public struct OpenListData : IComparable<OpenListData>
        {
            public int2 index;
            public int parentIndex;
            public float gCost;
            public float hCost;
            public float fCost;
            public int2 dir;
            public int nextIndex;

            public int CompareTo(OpenListData other)
            {
                if (fCost.CompareTo(other.fCost) != 0)
                {
                    return fCost.CompareTo(other.fCost);
                }
                else
                {
                    return hCost.CompareTo(other.hCost);
                }
            }
        }

        /// <summary>
        /// 初始化64位1
        /// </summary>
        public static void InitDic()
        {
            _hashMap = new NativeParallelHashMap<int, ulong>(64, Allocator.Persistent);
            int index = Definerow;
            ulong value = 1;
            while (index >= 0)
            {
                _hashMap.Add(index, value);
                value <<= 1;
                index--;
            }
        }
        
        public static int2 GetPosByNodeIndex(int2 index, int2 originPos)
        {
            int2 pos = new int2(index.y - Definerow / 2, Definerow / 2 - index.x) + originPos;
            return pos;
        }

        public readonly static int Definerow = 63; //测试为63x63，ulong位数-1
        //地图行信息
        public static NativeList<ulong> nativeListHorizontal;
        //地图列信息
        public static NativeList<ulong> nativeListVertical;
        //已经检测过的行
        private static NativeList<ulong> hasReachRow;
        //已经检测过的列
        private static NativeList<ulong> hasReachCol;
        //已经加入到OpenList的行格子
        private static NativeList<ulong> hasContainRow;
        //已经加入到openList的列格子
        private static NativeList<ulong> hasContainCol;
        public static void CreateJPSArray()
        {
            hasReachRow = new NativeList<ulong>(Definerow, Allocator.Persistent);
            hasReachCol = new NativeList<ulong>(Definerow, Allocator.Persistent);
            check1 = new ProfilerMarker("check1");
            check2 = new ProfilerMarker("check2");
            check3 = new ProfilerMarker("check3");
            check4 = new ProfilerMarker("check4");
            nativeListHorizontal = new NativeList<ulong>(Definerow, Allocator.Persistent);
            nativeListVertical = new NativeList<ulong>(Definerow, Allocator.Persistent);
            for (int i = 0; i < Definerow; i++)
            {
                nativeListHorizontal.AddNoResize(0);
                nativeListVertical.AddNoResize(0);
            }
        }

        /// <summary>
        /// 获取位置所在行数
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="originPos">中心位置</param>
        /// <returns></returns>
        public static int GetRowIndexByPos(int2 pos, int2 originPos)
        {
            return -pos.y + originPos.y + (Definerow / 2);
        }

        /// <summary>
        /// 获取位置所在列数
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="originPos"></param>
        /// <returns></returns>
        public static int GetColIndexByPos(int2 pos, int2 originPos)
        {
            return pos.x - originPos.x + (Definerow / 2);
        }

        /// <summary>
        /// 设置位置是否可达
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="originPos"></param>
        /// <param name="nativeListHorizontal"></param>
        /// <param name="nativeListVertical"></param>
        /// <param name="canReach"></param>
        public static void SetJPSNodeCanReach(int2 pos, int2 originPos,
            bool canReach = true)
        {
            var rowIndex = GetRowIndexByPos(pos, originPos);
            var colIndex = GetColIndexByPos(pos, originPos);
            _hashMap.TryGetValue(colIndex, out var valueHorizontal);
            _hashMap.TryGetValue(rowIndex, out var valueVertical);
            nativeListHorizontal[rowIndex] |= valueHorizontal;
            nativeListVertical[colIndex] |= valueVertical;
        }

        /// <summary>
        /// 当前位置相对上一个位置消耗
        /// </summary>
        /// <param name="curIndex"></param>
        /// <param name="parentIndex"></param>
        /// <returns></returns>
        public static int GetCost(int2 curIndex, int2 parentIndex)
        {
            int cost = math.abs(curIndex.x - parentIndex.x) + math.abs(curIndex.y - parentIndex.y);
            return cost;
        }

        /// <summary>
        /// 寻路开始，进行一些数据初始化
        /// </summary>
        /// <param name="curIndex"></param>
        /// <param name="originPos"></param>
        /// <param name="targetIndex"></param>
        /// <param name="resultIndex"></param>
        /// <returns></returns>
        public unsafe static OpenListData* JPSPathFinding(int2 curIndex, int2 originPos,
            int2 targetIndex, out int resultIndex)
        {
            hasReachRow.CopyFrom(nativeListHorizontal);
            hasReachCol.CopyFrom(nativeListVertical);
            BuildHeap();
            hasContainRow = new NativeList<ulong>(Definerow, Allocator.Persistent);
            hasContainCol = new NativeList<ulong>(Definerow, Allocator.Persistent);
            for (int i = 0; i < Definerow; i++)
            {
                hasContainRow.AddNoResize(0);
                hasContainCol.AddNoResize(0);
            }
            NativeList<OpenListData> closeList =
                new NativeList<OpenListData>(Definerow * Definerow, Allocator.Persistent);
            var index = new int2(GetRowIndexByPos(curIndex, originPos), GetColIndexByPos(curIndex, originPos));
            targetIndex = new int2(GetRowIndexByPos(targetIndex, originPos), GetColIndexByPos(targetIndex, originPos));
            //开始位置为中心位置
            int fCost = GetCost(new int2(Definerow / 2, Definerow / 2), targetIndex);
            var offset = targetIndex - index;
            int2 value;
            if (offset.x * offset.y == 0)
            {
                offset = offset.x + offset.y;
            }
            value = offset / math.abs(offset);
            //设置寻路方向
            NativeList<int2> tempList = new NativeList<int2>(4, Allocator.TempJob)
            {
                value, -value, new int2(-value.x, value.y), new int2(value.x, -value.y)
            };
            foreach (var VARIABLE in tempList)
            {
                var data = new OpenListData()
                {
                    index = index,
                    parentIndex = -1,
                    dir = VARIABLE,
                    fCost = fCost,
                    hCost = fCost
                };
                AddDataToHeap(data);
            }
            resultIndex = JPSPathFinding(targetIndex,
                ref closeList);
            unsafe
            {
                return (OpenListData*)closeList.GetUnsafePtr();
            }
        }


        static bool AddNodeToOpenList(int parentIndex, ref NativeList<OpenListData> closeList,
            int2 index, int2 targetIndex, int2 dir)
        {
            bool result = false;
            ulong value1, value2;
            //水平遍历
            if (dir.y > 0)
            {
                //水平正向
                result |= AddNodeToOpenList(ref nativeListHorizontal, ref closeList, parentIndex, index,
                    targetIndex, new int2(0, 1), TraversalDirection.Horizontal, Direction.Forward);
            }
            else
            {
                //水平逆向
                result |= AddNodeToOpenList(ref nativeListHorizontal, ref closeList, parentIndex, index,
                    targetIndex, new int2(0, -1), TraversalDirection.Horizontal, Direction.Reverse);
            }

            //Debug.Log($"index:{index} value1:{value1}  value2:{value2}  value:{value1 & value2}  row:{checkListRow[index.x]}");
            //垂直遍历
            if (dir.x > 0)
            {
                //垂直正向
                result |= AddNodeToOpenList(ref nativeListVertical, ref closeList, parentIndex,
                    index,
                    targetIndex, new int2(1, 0), TraversalDirection.Vertical, Direction.Forward);
            }
            else
            {
                //垂直逆向
                result |= AddNodeToOpenList(ref nativeListVertical, ref closeList, parentIndex, index,
                    targetIndex, new int2(-1, 0), TraversalDirection.Vertical, Direction.Reverse);
            }

            //Debug.Log($"index:{index} value1:{value1}  value2:{value2}  value:{value1 & value2}  col:{checkListCol[index.y]}");
            return result;
        }

        private static float2 minCost;
        private static int minCostIndex;

        /// <summary>
        /// 正式寻路
        /// </summary>
        /// <param name="targetIndex"></param>
        /// <param name="closeList"></param>
        /// <returns></returns>
        public static int JPSPathFinding(int2 targetIndex, ref NativeList<OpenListData> closeList)
        {
            if (openList.Length <= 0)
                return -1;
            bool findTarget = false;
            float2 value = int.MaxValue;
            minCost = int.MaxValue;
            minCostIndex = -1;
            do
            {
                OpenListData curNode;
                int2 curNodeIndex;
                using (check2.Auto())
                {
                    curNode = PoPTop();
                    value = new float2(curNode.fCost, curNode.hCost);
                    if (value.x >= minCost.x && value.y >= minCost.y)
                    {
                        return minCostIndex;
                    }
                    curNodeIndex = curNode.index;
                    closeList.AddNoResize(curNode);
                }

                var parentIndex = closeList.Length - 1;
                var dirIndex = 0;
                var time = GetTime(curNode.dir);
                while (time > 0)
                {
                    var RealDir = GetRealDir(time, curNode.dir);
                    curNodeIndex = curNode.index;
                    for (;
                         curNodeIndex.x >= 0 && curNodeIndex.x < Definerow
                                             && curNodeIndex.y >= 0 && curNodeIndex.y < Definerow;)
                    {
                        if (curNodeIndex.x == targetIndex.x && curNodeIndex.y == targetIndex.y)
                        {
                            var data = new OpenListData()
                            {
                                index = targetIndex,
                                parentIndex = parentIndex,
                            };
                            closeList.AddNoResize(data);
                            return closeList.Length - 1;
                        }

                        //检测是否是障碍
                        if ((_hashMap[curNodeIndex.y] & nativeListHorizontal[curNodeIndex.x]) > 0)
                        {
                            break;
                        }
                        //检测是否之前已经遍历过了
                        if (((hasReachRow[curNodeIndex.x] & _hashMap[curNodeIndex.y]) > 0
                             || (hasReachCol[curNodeIndex.y] & _hashMap[curNodeIndex.x]) > 0)
                            && curNodeIndex.x != 31)
                        {
                            break;
                        } ;
                        using (check1.Auto())
                        {
                            if (AddNodeToOpenList(parentIndex, ref closeList, curNodeIndex, targetIndex, RealDir))
                            {
                                //return minCostIndex;

                                break;
                            }
                        }
                        curNodeIndex += RealDir;
                    }
                    time--;
                }
            } while (heapDataLength >= 0);
            return minCostIndex;
        }

        public static bool AddNodeToOpenList(ref NativeList<ulong> nativeList, ref NativeList<OpenListData> closeList,
            int parentIndex, int2 index, int2 targetIndex,
            int2 dir, TraversalDirection direction, Direction direct)
        {
            var newIndex = direction == TraversalDirection.Horizontal ? index : new int2(index.y, index.x);
            var newTargetIndex = direction == TraversalDirection.Horizontal
                ? targetIndex
                : new int2(targetIndex.y, targetIndex.x);
            var binaryRow = GetBinaryRow(direct, nativeList[newIndex.x], newIndex.y);
            var length = GetLength(binaryRow, direct);
            var upDirection = GetNeighbor(newIndex.x - 1, ref nativeList, newIndex.y, direct);
            var downDirection = GetNeighbor(newIndex.x + 1, ref nativeList, newIndex.y, direct);
            ulong value = ulong.MaxValue;
            unsafe
            {
                var hasContainPtr = GetHasContainListByDirection(direction);
                value -= hasContainPtr[newIndex.x];
            }

            value = GetValue(value, newIndex.y, direct, (int) length);
            if (CheckIsTarget(direct, newIndex, newTargetIndex, (int) length))
            {
                var gCost = GetCost(targetIndex, index) + GetCost(index, closeList[parentIndex].index) +
                            closeList[parentIndex].gCost;
                var hCost = GetCost(targetIndex, targetIndex);
                var data = GetOpenListData(targetIndex, index, ref closeList, parentIndex, targetIndex);
                closeList.AddNoResize(data);
                if (gCost + hCost <= minCost.x && hCost < minCost.y)
                {
                    minCost = new float2(gCost + hCost, hCost);
                    minCostIndex = closeList.Length - 1;
                }

                return true;
            }

            var forwardDirection = upDirection | downDirection;
            uint forceNeighbor = GetForceNeighbor(forwardDirection, direct);

            unsafe
            {
                var hasContainPtr = GetHasContainListByDirection(direction);
                while (length > forceNeighbor && forwardDirection > 0)
                {
                    using (check3.Auto())
                    {
                        var moveStep = GetMoveStep(direct, (int) forceNeighbor);
                        int2 forceIndex = direction == TraversalDirection.Horizontal
                            ? new int2(index.x, moveStep)
                            : new int2(moveStep, index.y);
                        int2 newDir = default;
                        var tempValue = _hashMap[moveStep];
                        if (!((hasContainPtr[newIndex.x] & tempValue) > 1))
                        {
                            if ((upDirection & tempValue) > 0)
                            {
                                newDir += GetDir(direction, -1);
                            }

                            if ((downDirection & tempValue) > 0)
                            {
                                newDir += GetDir(direction, 1);
                            }

                            newDir += dir;
                            var data = GetOpenListData(forceIndex, index, ref closeList, parentIndex, targetIndex,
                                newDir);
                            AddDataToHeap(data);
                            hasContainRow[forceIndex.x] |= _hashMap[forceIndex.y];
                            hasContainCol[forceIndex.y] |= _hashMap[forceIndex.x];
                            value &= ulong.MaxValue - tempValue;
                        }

                        forwardDirection = GetForwardDirection(direct, forceNeighbor, forwardDirection);
                        forceNeighbor = GetForceNeighbor(forwardDirection, direct);
                    }
                }

                var hasReachPtr = GetHasReachListByDirection(direction);
                hasReachPtr[newIndex.x] |= value;
            }
            return false;
        }

        static OpenListData GetOpenListData(int2 forceIndex, int2 index, ref NativeList<OpenListData> closeList,
            int parentIndex, int2 targetIndex, int2 dir = default)
        {
            var gCost = GetCost(forceIndex, index) + math.abs(index.x - closeList[parentIndex].index.x) * 1.4f +
                        closeList[parentIndex].gCost;
            var hCost = GetCost(forceIndex, targetIndex);
            var data = new OpenListData()
            {
                index = forceIndex,
                parentIndex = parentIndex,
                gCost = gCost,
                hCost = hCost,
                fCost = gCost + hCost,
                dir = dir
            };
            return data;
        }
        static unsafe ulong* GetHasReachListByDirection(TraversalDirection direction)
        {
            return (ulong*)(direction == TraversalDirection.Horizontal
                ? hasReachRow.GetUnsafePtr()
                : hasReachCol.GetUnsafePtr());
        }

        static unsafe ulong* GetHasContainListByDirection(TraversalDirection direction)
        {
            return (ulong*) (direction == TraversalDirection.Horizontal
                ? hasContainRow.GetUnsafePtr()
                : hasContainCol.GetUnsafePtr());
        }

        static ulong GetNeighbor(int index, ref NativeList<ulong> nativeList, int len, Direction direction)
        {
            ulong neighbor = 0;
            if (index >= Definerow || index < 0)
                return 0;
            ulong temp;
            temp = neighbor = direction == Direction.Forward
                ? nativeList[index] << len >> len
                : nativeList[index] >> Definerow - len << Definerow - len;
            neighbor = direction == Direction.Forward ? neighbor >> 1 : neighbor << 1;
            neighbor &= ((ulong.MaxValue - 1) - temp);
            return neighbor;
        }

        static int2 GetDir(TraversalDirection direction, int value)
        {
            return direction == TraversalDirection.Horizontal ? new int2(value, 0) : new int2(0, value);
        }

        static ulong GetBinaryRow(Direction direction, ulong value, int len)
        {
            return direction == Direction.Forward
                ? value << len + 1 >> len + 1
                : value >> Definerow - len << Definerow - len;
        }

        static ulong GetValue(ulong value, int len, Direction direction, int length)
        {
            value = direction == Direction.Forward
                ? value << len + 1 >> len + 1
                : value >> Definerow - len << Definerow - len;
            if (Definerow - (int) length > 0)
                value = direction == Direction.Forward
                    ? value >> (int) (Definerow - length) << (int) (Definerow - length)
                    : value << (Definerow - (int) length) >> (Definerow - (int) length);
            return value;
        }

        static uint GetForceNeighbor(ulong forwardDirection, Direction direction)
        {
            return direction == Direction.Forward
                ? NumberOfLeadingZerosLong(forwardDirection)
                : NumberOfTrailingZerosLong(forwardDirection);
        }

        static int GetMoveStep(Direction direction, int forNeighbor)
        {
            return direction == Direction.Forward ? forNeighbor : Definerow - forNeighbor;
        }

        static uint GetLength(ulong binaryRow, Direction direction)
        {
            return direction == Direction.Forward
                ? NumberOfLeadingZerosLong(binaryRow)
                : NumberOfTrailingZerosLong(binaryRow);
        }

        static bool CheckIsTarget(Direction direction, int2 newIndex, int2 newTargetIndex, int length)
        {
            return direction == Direction.Forward
                ? newIndex.x == newTargetIndex.x && newTargetIndex.y - newIndex.y >= 0 &&
                  length > newTargetIndex.y
                : newIndex.x == newTargetIndex.x && newTargetIndex.y - newIndex.y < 0 &&
                  length > Definerow - newTargetIndex.y;
        }

        static ulong GetForwardDirection(Direction direction, ulong forceNeighbor, ulong forwardDirection)
        {
            return direction == Direction.Forward
                ? (forwardDirection << (int) forceNeighbor + 1) >> (int) forceNeighbor + 1
                : (forwardDirection >> (int) forceNeighbor + 1) << (int) forceNeighbor + 1;
        }

        //循环次数
        static int GetTime(int2 dir)
        {
            int result = 0;
            result = math.abs((math.abs(dir.x) - 1) * 2 + (math.abs(dir.y) - 1) * 2);
            return result == 0 ? 1 : result;
        }

        static int2 GetRealDir(int index, int2 dir)
        {
            if (dir.x != 0 && dir.y != 0)
                return dir;
            int time = 0;
            int value = 1;
            while (time < index)
            {
                value *= -1;
                time++;
            }

            return math.abs(dir.x) > 0 ? new int2(dir.x, value) : new int2(value, dir.y);
        }

        private static NativeList<OpenListData> openList;
        private static int heapDataLength = 0;

        /// <summary>
        /// 建堆
        /// </summary>
        static void BuildHeap()
        {
            openList = new NativeList<OpenListData>(3 * Definerow, Allocator.Persistent);
        }

        /// <summary>
        /// 数据加入
        /// </summary>
        /// <param name="data"></param>
        static void AddDataToHeap(OpenListData data)
        {
            openList.AddNoResize(data);
            heapDataLength = openList.Length - 1;
            AddOpenListData(heapDataLength);
        }

        static void AddOpenListData(int curIndex)
        {
            var parentIndex = (curIndex - 1) / 2;
            if (curIndex - 1 < 0)
                return;
            var parentData = openList[parentIndex];
            var curData = openList[curIndex];
            if (curData.CompareTo(parentData) < 0)
            {
                Swap(curIndex, parentIndex);
                AddOpenListData(parentIndex);
            }
        }

        static void Swap(int index1, int index2)
        {
            (openList[index1], openList[index2]) = (openList[index2], openList[index1]);
        }

        /// <summary>
        /// 返回当前最小消耗位置
        /// </summary>
        /// <returns></returns>
        static OpenListData PoPTop()
        {
            if (heapDataLength < 0)
            {
                return default;
            }

            var result = openList[0];
            openList[0] = openList[heapDataLength];
            openList.RemoveAt(heapDataLength);
            heapDataLength -= 1;
            BananceTree(0);
            return result;
        }

        /// <summary>
        /// 弹出堆顶后平衡树
        /// </summary>
        /// <param name="curIndex"></param>
        static void BananceTree(int curIndex)
        {
            var left = curIndex * 2 + 1;
            var right = curIndex * 2 + 2;
            var min = curIndex;
            if (left <= heapDataLength && openList[left].CompareTo(openList[min]) < 0)
            {
                min = left;
            }

            if (right <= heapDataLength && openList[right].CompareTo(openList[min]) < 0)
            {
                min = right;
            }

            if (min != curIndex)
            {
                Swap(curIndex, min);
                BananceTree(min);
            }
        }
    }
}