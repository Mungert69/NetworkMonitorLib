using System;
using System.IO;

public class Logo
{
    private string _filePath="./logo.txt";

    // Constructor that takes the file path as a parameter
    public Logo()
    {
        DisplayFileContents();
    }

    public string FilePath { get => _filePath; set => _filePath = value; }

    // Method to read the file and display its contents
    private void DisplayFileContents()
    {
        try
        {
            string fileContents = File.ReadAllText(FilePath);
            Console.WriteLine(fileContents);
        }
       
        catch 
        {
            // just carry on as its not important to display the logo
        }
    }
}
