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
export interface ModNode { name: string; count: number; prefabs: PrefabNode[] }
export interface IssueGroup { category: string; count: number; fixes: string[]; mods: ModNode[] }
export interface IssueTree {
    error?: string | null;
    result?: string | null;
    scanned?: boolean;
    total?: number;
    brokenNodes?: number;
    brokenCursor?: number;
    groups?: IssueGroup[];
}
export interface SurfaceOption { id: string; name: string }

const open$ = bindValue<boolean>(G, "open", false);
const notice$ = bindValue<boolean>(G, "notice", false);
const tree$ = bindValue<string>(G, "tree", "{}");
const surfaces$ = bindValue<string>(G, "surfaces", "[]");

function parse<T>(raw: string, fallback: T): T {
    try {
        return JSON.parse(raw) as T;
    } catch {
        return fallback;
    }
}

export const useOpen = () => useValue(open$);
export const useNotice = () => useValue(notice$);

export function useTree(): IssueTree {
    const raw = useValue(tree$);
    return useMemo(() => parse<IssueTree>(raw, {}), [raw]);
}

export function useSurfaces(): SurfaceOption[] {
    const raw = useValue(surfaces$);
    return useMemo(() => parse<SurfaceOption[]>(raw, []), [raw]);
}

export const actions = {
    setOpen: (value: boolean) => trigger(G, "setOpen", value),
    dismissNotice: () => trigger(G, "dismissNotice"),
    rescan: () => trigger(G, "rescan"),
    step: (id: string, delta: number) => trigger(G, "step", id, delta),
    stepNode: (delta: number) => trigger(G, "stepNode", delta),
    changeBrands: () => trigger(G, "changeBrands"),
    removeSurfaces: () => trigger(G, "removeSurfaces"),
    replaceSurfaces: (id: string) => trigger(G, "replaceSurfaces", id),
};
