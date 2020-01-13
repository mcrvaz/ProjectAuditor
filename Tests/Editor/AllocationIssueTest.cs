using System.Linq;
using NUnit.Framework;
using Unity.ProjectAuditor.Editor;

namespace UnityEditor.ProjectAuditor.EditorTests
{
    public class AllocationIssueTest
    {
        private ScriptResource m_ScriptResourceValueTypeAllocation;
        private ScriptResource m_ScriptResourceObjectAllocation;
        private ScriptResource m_ScriptResourceArrayAllocation;

        [OneTimeSetUp]
        public void SetUp()
        {
            m_ScriptResourceValueTypeAllocation = new ScriptResource("ValueTypeAllocation.cs", @"
struct ValueType
{
    public static ValueType Make()
    {
        return new ValueType();
    }
}
");

            m_ScriptResourceObjectAllocation = new ScriptResource("ObjectAllocation.cs", @"
class ObjectAllocation
{
    public static ObjectAllocation Make()
    {
        return new ObjectAllocation();
    }
}
");
            m_ScriptResourceArrayAllocation = new ScriptResource("ArrayAllocation.cs", @"
class MyParam
{
}

class ArrayAllocation
{
    MyParam param;
    public void MethodWithParams(params MyParam[] list)
    {
    }

    public void Dummy()
    {
        MethodWithParams(param);
    }
}
");
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            m_ScriptResourceValueTypeAllocation.Delete();
            m_ScriptResourceObjectAllocation.Delete();
            m_ScriptResourceArrayAllocation.Delete();
        }

        [Test]
        public void ValueTypeAllocationIsNotReported()
        {
            var issues =
                ScriptIssueTestHelper.AnalyzeAndFindScriptIssues(m_ScriptResourceValueTypeAllocation.relativePath);

            Assert.Zero(issues.Count());
        }

        [Test]
        public void ObjectAllocationIsReported()
        {
            var issues = ScriptIssueTestHelper.AnalyzeAndFindScriptIssues(m_ScriptResourceObjectAllocation.relativePath);
			
            Assert.AreEqual(1, issues.Count());
			
            var allocIssue = issues.FirstOrDefault();
			
            Assert.NotNull(allocIssue);
            Assert.NotNull(allocIssue.descriptor);

            // check descriptor
            Assert.AreEqual(Rule.Action.Default, allocIssue.descriptor.action);
            Assert.AreEqual(102002, allocIssue.descriptor.id);
            Assert.True(string.IsNullOrEmpty(allocIssue.descriptor.type));
            Assert.True(string.IsNullOrEmpty(allocIssue.descriptor.method));
            Assert.False(string.IsNullOrEmpty(allocIssue.descriptor.description));
            Assert.True(allocIssue.descriptor.description.Equals("Object Allocation"));

            // check issue
            Assert.True(allocIssue.name.Equals("ObjectAllocation.Make"));
            Assert.True(allocIssue.filename.Equals(m_ScriptResourceObjectAllocation.scriptName));
            Assert.True(allocIssue.description.Equals(".ctor object allocation"));
            Assert.True(allocIssue.callingMethod.Equals("ObjectAllocation ObjectAllocation::Make()"));
            Assert.AreEqual(6, allocIssue.line);
            Assert.AreEqual(IssueCategory.ApiCalls, allocIssue.category);
        } 
        
        [Test]
        public void ArrayAllocationIsReported()
        {
            var issues = ScriptIssueTestHelper.AnalyzeAndFindScriptIssues(m_ScriptResourceArrayAllocation.relativePath);
			
            Assert.AreEqual(1, issues.Count());
			
            var allocIssue = issues.FirstOrDefault();
			
            Assert.NotNull(allocIssue);
            Assert.NotNull(allocIssue.descriptor);

            // check descriptor
            Assert.AreEqual(Rule.Action.Default, allocIssue.descriptor.action);
            Assert.AreEqual(102003, allocIssue.descriptor.id);
            Assert.True(string.IsNullOrEmpty(allocIssue.descriptor.type));
            Assert.True(string.IsNullOrEmpty(allocIssue.descriptor.method));
            Assert.False(string.IsNullOrEmpty(allocIssue.descriptor.description));
            Assert.True(allocIssue.descriptor.description.Equals("Array Allocation"));

            // check issue
            Assert.True(allocIssue.name.Equals("ArrayAllocation.Dummy"));
            Assert.True(allocIssue.filename.Equals(m_ScriptResourceArrayAllocation.scriptName));
            Assert.True(allocIssue.description.Equals("MyParam array allocation"));
            Assert.True(allocIssue.callingMethod.Equals("System.Void ArrayAllocation::Dummy()"));
            Assert.AreEqual(15, allocIssue.line);
            Assert.AreEqual(IssueCategory.ApiCalls, allocIssue.category);
        } 
    }
}