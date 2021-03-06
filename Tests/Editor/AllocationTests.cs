using System;
using System.Linq;
using NUnit.Framework;
using Unity.ProjectAuditor.Editor;

namespace UnityEditor.ProjectAuditor.EditorTests
{
    class AllocationTests
    {
        ScriptResource m_ScriptResourceObjectAllocation;
        ScriptResource m_ScriptResourceArrayAllocation;
        ScriptResource m_ScriptResourceMultidimensionalArrayAllocation;
        ScriptResource m_ScriptResourceParamsArrayAllocation;

        [OneTimeSetUp]
        public void SetUp()
        {
            m_ScriptResourceObjectAllocation = new ScriptResource("ObjectAllocation.cs", @"
class ObjectAllocation
{
    static ObjectAllocation Dummy()
    {
        // explicit object allocation
        return new ObjectAllocation();
    }
}
");

            m_ScriptResourceArrayAllocation = new ScriptResource("ArrayAllocation.cs", @"
class ArrayAllocation
{
    int[] array;
    void Dummy()
    {
        // explicit array allocation
        array = new int[1];
    }
}
");

            m_ScriptResourceMultidimensionalArrayAllocation = new ScriptResource("MultidimensionalArrayAllocation.cs", @"
class MultidimensionalArrayAllocation
{
    int[,] array;
    void Dummy()
    {
        // explicit array allocation
        array = new int[1,1];
    }
}
");

            m_ScriptResourceParamsArrayAllocation = new ScriptResource("ParamsArrayAllocation.cs", @"
class ParamsArrayAllocation
{
    void DummyImpl(params object[] args)
    {
    }

    void Dummy(object C)
    {
        // implicit array allocation
        DummyImpl(null, null);
    }
}
");
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            m_ScriptResourceObjectAllocation.Delete();
            m_ScriptResourceArrayAllocation.Delete();
        }

        [Test]
        public void ObjectAllocationIsFound()
        {
            var issues = ScriptIssueTestHelper.AnalyzeAndFindScriptIssues(m_ScriptResourceObjectAllocation);

            Assert.AreEqual(1, issues.Count());

            var allocationIssue = issues.FirstOrDefault();

            Assert.NotNull(allocationIssue);
            Assert.True(allocationIssue.description.Equals("'ObjectAllocation' object allocation"));
            Assert.AreEqual(IssueCategory.Code, allocationIssue.category);
        }

        [Test]
        public void ArrayAllocationIsFound()
        {
            var issues = ScriptIssueTestHelper.AnalyzeAndFindScriptIssues(m_ScriptResourceArrayAllocation);
            Assert.AreEqual(1, issues.Count());

            var allocationIssue = issues.FirstOrDefault();

            Assert.NotNull(allocationIssue);
            Assert.True(allocationIssue.description.Equals("'Int32' array allocation"));
            Assert.AreEqual(IssueCategory.Code, allocationIssue.category);
        }

        [Test]
        public void MultidimensionalArrayAllocationIsFound()
        {
            var issues = ScriptIssueTestHelper.AnalyzeAndFindScriptIssues(m_ScriptResourceMultidimensionalArrayAllocation);
            Assert.AreEqual(1, issues.Count());

            var allocationIssue = issues.FirstOrDefault();

            Assert.NotNull(allocationIssue);
            Assert.True(allocationIssue.description.Equals("'Int32[0...,0...]' object allocation"));
            Assert.AreEqual(IssueCategory.Code, allocationIssue.category);
        }

        [Test]
        public void ParamsArrayAllocationIsFound()
        {
            var issues = ScriptIssueTestHelper.AnalyzeAndFindScriptIssues(m_ScriptResourceParamsArrayAllocation);
            Assert.AreEqual(1, issues.Count());

            var allocationIssue = issues.FirstOrDefault();

            Assert.NotNull(allocationIssue);
            Assert.True(allocationIssue.description.Equals("'Object' array allocation"));
            Assert.AreEqual(IssueCategory.Code, allocationIssue.category);
        }
    }
}
