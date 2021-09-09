﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UXM
{
    //Partially my own shitcode, partially shitcode from Stack Overflow
    public partial class FormFileView : Form
    {
        private static string Prefix;

        private static TreeView currentNodes = new TreeView();

        private FormMain Parent;

        public FormFileView(FormMain parent)
        {
            InitializeComponent();
            Parent = parent;
            fileTreeView.Nodes.Add((TreeNode)currentNodes.Nodes[0].Clone());
        }
        


        public static void PopulateTreeview(string exePath)
        {
            Util.Game game;
            if (File.Exists(exePath))
                game = Util.GetExeVersion(exePath);
            else
                return;

            currentNodes.Nodes.Clear();

            Prefix = GameInfo.GetPrefix(game);

#if DEBUG
            var fileList = File.ReadAllLines($@"..\..\dist\res\{Prefix}Dictionary.txt").ToArray();

#else
            var fileList = File.ReadAllLines($@"{GameInfo.ExeDir}\res\{Prefix}Dictionary.txt").ToArray();
#endif
            currentNodes.Nodes.Add(PopulateTreeNode2(fileList, @"/", Prefix));
        }

        private static TreeNode PopulateTreeNode2(string[] paths, string pathSeparator, string prefix)
        {
            if (paths == null)
                return null;

            TreeNode thisnode = new TreeNode(prefix);
            TreeNode currentnode;
            char[] cachedpathseparator = pathSeparator.ToCharArray();
            foreach (string path in paths)
            {
                currentnode = thisnode;
                foreach (string subPath in path.Split(cachedpathseparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (null == currentnode.Nodes[subPath])
                        currentnode = currentnode.Nodes.Add(subPath, subPath);
                    else
                        currentnode = currentnode.Nodes[subPath];
                }
            }

            return thisnode;
        }


        private void fileTreeView_AfterCheck(object sender, TreeViewEventArgs e)
        {
            try
            {
                e.Node.TreeView.BeginUpdate();
                if (e.Node.Nodes.Count > 0)
                {
                    var parentNode = e.Node;
                    var nodes = e.Node.Nodes;
                    CheckedOrUnCheckedNodes(parentNode, nodes);
                }
            }
            finally
            {
                e.Node.TreeView.EndUpdate();
            }
        }

        private void CheckedOrUnCheckedNodes(TreeNode parentNode, TreeNodeCollection nodes)
        {
            if (nodes.Count > 0)
            {
                foreach (TreeNode node in nodes)
                {
                    node.Checked = parentNode.Checked;
                    CheckedOrUnCheckedNodes(parentNode, node.Nodes);
                }
            }
        }

        private void btnSelectAll_Click(object sender, EventArgs e)
        {
            
            CheckAllNodes(fileTreeView.Nodes, true);
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            CheckAllNodes(fileTreeView.Nodes, false);
        }

        public void CheckAllNodes(TreeNodeCollection nodes, bool isChecked)
        {
            nodes[0].TreeView.BeginUpdate();
            foreach (TreeNode node in nodes)
            {
                node.Checked = isChecked;
                if (node.Nodes.Count > 0)
                    CheckChildren(node, isChecked);
            }
            nodes[0].TreeView.EndUpdate();
        }

        private void CheckChildren(TreeNode rootNode, bool isChecked)
        {
            foreach (TreeNode node in rootNode.Nodes)
            {
                if (node.Nodes.Count > 0)
                    CheckChildren(node, isChecked);
                node.Checked = isChecked;
            }
        }

        public static List<string> SelectedFiles = new List<string>();

        private void btnOk_Click(object sender, EventArgs e)
        {
            SelectedFiles.Clear();
            AddSelectedFiles(fileTreeView.Nodes);
            currentNodes.Nodes.Clear();
            Parent.cbxSkip.Checked = SelectedFiles.Any();
            currentNodes.Nodes.Add((TreeNode)fileTreeView.Nodes[0].Clone());
            Close();
        }

        private void AddSelectedFiles(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Nodes.Count > 0)
                    AddSelectedFiles(node.Nodes);
                else
                    if (node.Checked) SelectedFiles.Add(node.FullPath.Replace(Prefix, ""));
            }
        }
    }
}