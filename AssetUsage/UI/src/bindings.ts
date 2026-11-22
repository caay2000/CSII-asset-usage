import { useMemo } from "react";
import { bindValue, trigger, useValue } from "cs2/api";

const G = "AssetUsage.LoadIssues";

export interface PrefabNode {
    id: string;
    name: string;
    located: number;
    unlocated: number;
    cursor: number;
    keptAliveBy: string[];
}
export interface TypeNode { category: string; count: number; prefabs: PrefabNode[] }
export interface ModNode { name: string; count: number; types: TypeNode[] }
export interface IssueTree {
    error?: string | null;
    scanned?: boolean;
    total?: number;
    brokenNodes?: number;
    brokenCursor?: number;
    mods?: ModNode[];
}

const open$ = bindValue<boolean>(G, "open", false);
const tree$ = bindValue<string>(G, "tree", "{}");

export const useOpen = () => useValue(open$);

export function useTree(): IssueTree {
    const raw = useValue(tree$);
    return useMemo(() => {
        try {
            return JSON.parse(raw) as IssueTree;
        } catch {
            return {};
        }
    }, [raw]);
}

export const actions = {
    setOpen: (value: boolean) => trigger(G, "setOpen", value),
    rescan: () => trigger(G, "rescan"),
    step: (id: string, delta: number) => trigger(G, "step", id, delta),
    stepNode: (delta: number) => trigger(G, "stepNode", delta),
};
