using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

using UnityEngine;

public class testwork : MonoBehaviour, INotifyPropertyChanged
{
    
    [SerializeField]
    private int test1;
    public int Test1
    {
        get
        {
            return test1;
        }
        set
        {
            test1 = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(test1)));
        }
    }
    
    [SerializeField]
    private float testFloat;
    public float TestFloat
    {
        get
        {
            return testFloat;
        }
        set
        {
            testFloat = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(testFloat)));
        }
    }
    
    [SerializeField]
    private float testFloat1;
    public float TestFloat1
    {
        get
        {
            return testFloat1;
        }
        set
        {
            testFloat1 = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(testFloat1)));
        }
    }
    
    [SerializeField]
    private float testFloat2;
    public float TestFloat2
    {
        get
        {
            return testFloat2;
        }
        set
        {
            testFloat2 = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(testFloat2)));
        }
    }
    
    public event PropertyChangedEventHandler PropertyChanged;

}
