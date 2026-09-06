"use client";

import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@st/ui/components/ui/breadcrumb";
import Link from "next/link";
import { useSelectedLayoutSegments } from "next/navigation";
import { Fragment } from "react";

const labels: Record<string, string> = {};

export function DynamicBreadcrumb() {
  const segments = useSelectedLayoutSegments();

  return (
    <Breadcrumb>
      <BreadcrumbList>
        <BreadcrumbItem>
          {segments.length === 0 ? (
            <BreadcrumbPage>Dashboard</BreadcrumbPage>
          ) : (
            <BreadcrumbLink asChild>
              <Link href="/">Dashboard</Link>
            </BreadcrumbLink>
          )}
        </BreadcrumbItem>
        {segments.map((segment, index) => {
          const isLast = index === segments.length - 1;
          const label = labels[segment] ?? segment;

          return (
            <Fragment key={segment}>
              <BreadcrumbSeparator />
              <BreadcrumbItem>
                {isLast ? <BreadcrumbPage>{label}</BreadcrumbPage> : label}
              </BreadcrumbItem>
            </Fragment>
          );
        })}
      </BreadcrumbList>
    </Breadcrumb>
  );
}
