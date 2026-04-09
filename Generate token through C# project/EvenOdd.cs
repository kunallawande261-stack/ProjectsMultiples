
/*You are given an array of integers A of size N.
Return the difference between the maximum among all even numbers of A and the minimum among all odd numbers in A. */
using System;

public class FindDiff
{
    public static int diff(int[] a)
    {
        
        int MinOdd=int.MaxValue,MaxEven=int.MinValue;
        
        for(int i=0;i<a.Length;i++)
        {  
            int num=a[i];
            if(num%2==0)
            {
                if(num>MaxEven)
                {
                    MaxEven=num;
                }
            }
            else
            {
                if(num<MinOdd)
                {
                    MinOdd=num;
                }
            }
        }
       
        return (MaxEven-MinOdd);
    }
    
    public static void Main(string[] args)
    {
        
        int[] a={0, 2, 9};
        
        Console.WriteLine ("Diffrence between max even number and min even nuumber : "+diff(a));
        
    }
}