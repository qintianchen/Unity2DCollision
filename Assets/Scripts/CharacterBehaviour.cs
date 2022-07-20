using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class CharacterBehaviour : MonoBehaviour
{
    public float speed;
    public float radius;
    public BulletBehaviour bulletPrefab;
    public float fireCD;

    private float lastFireTime;
    private Vector2 velocity; // 速度
    private List<RaycastHit2D> hitToDraw = new();
    private Vector2 centerNormal = Vector2.zero;

    void FixedUpdate()
    {
        hitToDraw.Clear();
        
        velocity = Vector2.zero;
        if (Input.GetKey(KeyCode.W))
        {
            velocity.y = 1;
        }
        else if(Input.GetKey(KeyCode.S))
        {
            velocity.y = -1;
        }
        
        if (Input.GetKey(KeyCode.A))
        {
            velocity.x = -1;
        }
        else if(Input.GetKey(KeyCode.D))
        {
            velocity.x = 1;
        }

        velocity *= speed;
        
        Move(Time.fixedDeltaTime);
    }

    private void Update()
    {
        if (Input.GetMouseButton(0) && Time.time - lastFireTime > fireCD)
        {
            lastFireTime = Time.time;
            Fire(Input.mousePosition - Camera.main.WorldToScreenPoint(transform.position));
        }
    }

    private void Fire(Vector2 direction)
    {
        var bulletGo = Instantiate(bulletPrefab);
        bulletGo.transform.position = transform.position;
        bulletGo.velocity = direction.normalized * 10;
        bulletGo.gameObject.SetActive(true);
    }

    // 根据当前速度，运动时间 t
    void Move(float t, int count = 0)
    {
        if (Mathf.Abs(t - 0) <= float.Epsilon) return;  // 运行时间太短
        if (Mathf.Abs(0 - velocity.magnitude) <= float.Epsilon) return;      // 速度太小
        if (count > 10) return;                         // 迭代次数太多
        
        Vector2 posOrigin = transform.position;
        Vector2 offset = t * velocity;
        // Debug.Log($"velocity={velocity},t={t},offset={offset},count={count}");

        RaycastHit2D[] hits = Physics2D.CircleCastAll(posOrigin, radius, velocity, offset.magnitude);

        // 没有碰撞
        if (hits.Length == 0)
        {
            transform.position += (Vector3) offset;
            return;
        }

        // 未来有碰撞
        List<RaycastHit2D> allHits = hits.ToList();             // 所有的待检测的碰撞体
        List<RaycastHit2D> contactList = new(allHits.Count);    // 所有当前接触的碰撞体
        
        // 按照距离排序碰撞体
        allHits.Sort((hit1, hit2) =>
        {
            if (Math.Abs(hit1.distance - hit2.distance) < float.Epsilon) return 0;
            if (hit1.distance < hit2.distance) return -1;
            return 1;
        });

        int iterationCount = 0;
        while (true)
        {
            iterationCount++;
            if (iterationCount > 5)
            {
                Debug.LogError($"while 超过了最大的迭代速度");
                return;
            }
            
            contactList.Clear();
            RaycastHit2D firstHitUncontact = new RaycastHit2D();    // 第一个未接触的碰撞体
            
            // 获取所有接触的碰撞体
            for (var i = 0; i < allHits.Count; i++)
            {
                var hit = allHits[i];
                if (Mathf.Abs(hit.distance - 0) < float.Epsilon)
                {
                    contactList.Add(allHits[i]);
                }
                else if (firstHitUncontact.collider == null)
                {
                    firstHitUncontact = hit;
                }
            }

            if (contactList.Count == 0)
            {
                // 没有接触的碰撞体，直接运动到下一个碰撞体
                if (firstHitUncontact.collider == null)
                {
                    // Debug.Log($"1111");
                    transform.position += (Vector3) offset;
                    break;
                }
                else
                {
                    // Debug.Log($"2222 ： {iterationCount}");
                    transform.position = firstHitUncontact.centroid;
                
                    // 修正时间
                    float t2 = (posOrigin - (Vector2) transform.position).magnitude / offset.magnitude * t;
                
                    // 修正速度
                    velocity -= Vector2.Dot(velocity, firstHitUncontact.normal) * firstHitUncontact.normal;
                
                    // 下一次迭代
                    Move(t2);
                    break;
                }
            }
            else if (contactList.Count == 1)
            { 
                // Debug.Log($"3333 ： {iterationCount}");
                // 只有一个接触体，此时我们先修正速度，然后视该碰撞体不存在，再做一个运行预测
                RaycastHit2D hit = contactList[0];
                hitToDraw.Add(hit);
                // transform.position = hit.centroid;
                SetPositionByHitCentroid(hit);

                if (Vector2.Dot(velocity, hit.normal) < 0)
                {
                    velocity -= Vector2.Dot(velocity, hit.normal) * hit.normal;
                }

                offset = velocity * t;
                allHits.Remove(hit);
            }
            else
            {
                // Debug.Log($"multiple collider {contactList.Count} ： {iterationCount}");
                hitToDraw.AddRange(contactList);
                
                // 计算中心法线
                Vector2 centerNormal = Vector2.zero;
                for (int i = 0; i < contactList.Count; i++)
                {
                    centerNormal += -contactList[i].normal;
                    centerNormal = centerNormal.normalized;
                }
                centerNormal /= contactList.Count;
                centerNormal = centerNormal.normalized;

                this.centerNormal = centerNormal;

                // 计算最大的角度值
                float maxAngle = 0;
                Vector2 maxNormal = Vector2.zero;
                for (int i = 0; i < contactList.Count; i++)
                {
                    var angle = Vector2.Angle(-contactList[i].normal, centerNormal);
                    if (angle > maxAngle)
                    {
                        maxNormal = -contactList[i].normal;
                        maxAngle = angle;
                    }
                }

                float curAngle = Vector2.Angle(centerNormal, velocity);
                // Debug.Log($"curAngle : maxAngle = {curAngle} : {maxAngle}, {velocity}, {maxNormal}");
                if (curAngle <= maxAngle)
                {
                    // 当前角度小于最大角度，则行动被限制
                    velocity = Vector2.zero;
                    break;
                }
                else
                {
                    // 遍历得到最小角度的向量
                    float minAngle = float.MaxValue;
                    RaycastHit2D minHit = new RaycastHit2D();
                    for (int i = 0; i < contactList.Count; i++)
                    {
                        float angle = Vector2.Angle(velocity, -contactList[i].normal);
                        if (angle < minAngle)
                        {
                            minAngle = angle;
                            minHit = contactList[i];
                        }
                    }
                    
                    if (minHit.collider == null)
                    {
                        // 怎么会是Null呢？
                        // Debug.Log($"怎么找不到最小向量呢？");
                        break;
                    }

                    // Debug.Log($"找到可以修正的墙体：{minHit.collider.gameObject.name} {minHit.centroid}");

                    // 修正速度，排除接触了的墙体，进入下一轮
                    SetPositionByHitCentroid(minHit);
                    // transform.position = minHit.centroid;
                    velocity -= Vector2.Dot(velocity, minHit.normal) * minHit.normal;
                    offset = velocity * t;

                    for (int i = 0; i < contactList.Count; i++)
                    {
                        allHits.Remove(contactList[i]);
                    }
                }
            }
            
        }
    }

    private void SetPositionByHitCentroid(RaycastHit2D hit)
    {
        if ((hit.point - hit.centroid).magnitude < radius)
        {
            transform.position = hit.point + hit.normal * radius;
        }
        else
        {
            transform.position = hit.centroid;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawLine(transform.position, transform.position + (Vector3) velocity.normalized);
        Gizmos.color = Color.red;
        for (var i = 0; i < hitToDraw.Count; i++)
        {
            var hit = hitToDraw[i];
            Gizmos.DrawLine(hit.point, hit.point + hit.normal);
        }
        Gizmos.color = Color.white;
        
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(transform.position, transform.position + (Vector3) centerNormal);
        Gizmos.color = Color.white;
    }
}