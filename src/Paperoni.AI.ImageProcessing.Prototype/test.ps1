#dotnet run -- --input ../../FakeDocuments/Fake1.png --auto-levels
#dotnet run -- --input ../../FakeDocuments/Fake1_cropped.png --auto-levels-only --clahe-clip 1.2
#dotnet run -- --input ../../FakeDocuments/Fake1_cropped.png --auto-levels-only --histogram
#dotnet run -- --input ../../FakeDocuments/Fake2.png --auto-levels --histogram
dotnet run -- --input ../../FakeDocuments/Fake1.png --auto-levels --histogram
#--adaptive-threshold --threshold-c 6.0 --threshold-block 66

